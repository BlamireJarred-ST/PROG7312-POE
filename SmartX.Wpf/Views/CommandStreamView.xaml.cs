using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SmartX.Shared.Models;
using SmartX.Shared.Services;

namespace SmartX.Wpf;

// Real-Time Command Stream and History dashboard.
// Polls the API asynchronously (never blocking the UI thread), keeps its selection / filters / history
// while the user visits other modules, and lets the operator issue manual override commands.
public partial class CommandStreamView : UserControl, INavigableView
{
    // camelCase + case-insensitive, matching ASP.NET Core's web defaults
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(350) };

    private bool _ready;
    private bool _refreshing;
    private bool _suppressSelection;
    private string? _selectedMac;
    private string? _lastLoggedSearch;
    private DeviceState? _selectedDevice;
    private List<TelemetryRecord> _timeline = new();

    public CommandStreamView()
    {
        InitializeComponent();

        CategoryFilter.ItemsSource = new[] { "All Categories", "Environmental", "PowerConsumption", "Actuator" };
        CategoryFilter.SelectedIndex = 0;

        StateFilter.ItemsSource = new[] { "All States", "Normal", "Warning", "Critical", "Offline" };
        StateFilter.SelectedIndex = 0;

        _refreshTimer.Tick += async (_, _) => await RefreshAsync();

        _searchDebounce.Tick += async (_, _) =>
        {
            _searchDebounce.Stop();
            await RefreshAsync();
        };

        _ready = true;
    }

    // ------------------------------------------------------------------ navigation

    public async Task OnNavigatedToAsync()
    {
        _refreshTimer.Start();
        await RefreshAsync();
    }

    // Polling pauses while the user is on another module; all state is kept in this object
    public void OnNavigatedFrom() => _refreshTimer.Stop();

    public void Shutdown()
    {
        _refreshTimer.Stop();
        _searchDebounce.Stop();
    }

    // ------------------------------------------------------------------ polling

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;

        try
        {
            var devicesTask = GetAsync<List<DeviceState>>("/api/devices" + BuildDeviceQuery());
            var suggestionsTask = GetAsync<List<Suggestion>>("/api/suggestions");
            var statusTask = GetAsync<PipelineStatus>("/api/pipeline/status");
            var stackTask = GetAsync<List<OverrideCommand>>("/api/commands/stack");
            var historyTask = GetAsync<List<OverrideCommand>>("/api/commands/history");
            var alertsTask = GetAsync<AlertsSnapshot>("/api/alerts");

            await Task.WhenAll(devicesTask, suggestionsTask, statusTask, stackTask, historyTask, alertsTask);

            SetConnection(true);

            var devices = devicesTask.Result ?? new List<DeviceState>();
            var suggestions = suggestionsTask.Result ?? new List<Suggestion>();

            // Highlight devices the Action Engine thinks are problematic
            var flagged = suggestions
                .Where(s => s.DeviceId != null && s.Kind == "Insight")
                .Select(s => s.DeviceId!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var device in devices)
            {
                device.Flagged = flagged.Contains(device.MacAddress);
            }

            ShowDevices(devices);
            ShowSuggestions(suggestions);
            ShowStatus(statusTask.Result);
            ShowCommands(stackTask.Result ?? new(), historyTask.Result ?? new());
            ShowAlerts(alertsTask.Result);

            await LoadTimelineAsync();
        }
        catch (Exception)
        {
            // API down or restarting: keep showing the last data, never crash
            SetConnection(false);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static async Task<T?> GetAsync<T>(string url)
    {
        using var response = await App.ApiClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(Json);
    }

    private string BuildDeviceQuery()
    {
        var parts = new List<string>();

        string search = SearchBox.Text.Trim();
        if (search.Length > 0) parts.Add("search=" + Uri.EscapeDataString(search));

        if (CategoryFilter.SelectedIndex > 0) parts.Add("category=" + CategoryFilter.SelectedItem);
        if (StateFilter.SelectedIndex > 0) parts.Add("state=" + StateFilter.SelectedItem);

        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    // ------------------------------------------------------------------ rendering

    private void SetConnection(bool connected)
    {
        ConnectionText.Text = connected ? "● API connected" : "● API offline (retrying)";
        ConnectionText.Foreground = connected ? Brushes.ForestGreen : Brushes.IndianRed;
    }

    private void ShowDevices(List<DeviceState> devices)
    {
        // Replacing the ItemsSource clears the selection; restore it by MAC without re-triggering loads
        _suppressSelection = true;

        DevicesGrid.ItemsSource = devices;

        _selectedDevice = _selectedMac == null
            ? null
            : devices.FirstOrDefault(d => d.MacAddress.Equals(_selectedMac, StringComparison.OrdinalIgnoreCase));

        DevicesGrid.SelectedItem = _selectedDevice;

        _suppressSelection = false;

        UpdateSelectedHeader();
    }

    private void UpdateSelectedHeader()
    {
        if (_selectedDevice == null)
        {
            SelectedDeviceText.Text = _selectedMac == null
                ? "Select a device in the grid"
                : "Selected device is hidden by the current filter";
            OverridePanel.IsEnabled = false;
            return;
        }

        SelectedDeviceText.Text =
            $"{_selectedDevice.NodeId}  ({_selectedDevice.MacAddress})\n{_selectedDevice.OutputSummary}";
        OverridePanel.IsEnabled = true;
    }

    private void ShowSuggestions(List<Suggestion> suggestions)
    {
        // keep the user's selected suggestion across refreshes
        string? selectedTitle = (SuggestionsList.SelectedItem as Suggestion)?.Title;

        SuggestionsList.ItemsSource = suggestions;

        if (selectedTitle != null)
        {
            SuggestionsList.SelectedItem = suggestions.FirstOrDefault(s => s.Title == selectedTitle);
        }
    }

    private void ShowStatus(PipelineStatus? status)
    {
        if (status == null) return;

        QueueText.Text =
            $"FIFO queue: {status.StandardQueueDepth}   Priority queue: {status.PriorityQueueDepth}   " +
            $"Processed: {status.TotalProcessed}   Active errors: {status.ActiveErrorCount}";

        PauseButton.Content = status.Paused ? "Resume Pipeline" : "Pause Pipeline";

        PipelineList.ItemsSource = status.RecentlyProcessed
            .AsEnumerable()
            .Reverse()
            .Select(p => $"#{p.Order,-4} {p.Lane,-8} {p.DeviceId,-17} {p.PacketType,-12} {p.Value,-8} {p.Severity}")
            .ToList();
    }

    private void ShowCommands(List<OverrideCommand> stack, List<OverrideCommand> history)
    {
        HistoryList.ItemsSource = history
            .Select(c => $"{c.IssuedAtUtc.ToLocalTime():HH:mm:ss}  {c.Description}")
            .ToList();

        StackList.ItemsSource = stack
            .Select((c, i) => $"{(i == 0 ? "TOP ►" : "     ")} {c.Description}")
            .ToList();

        UndoButton.IsEnabled = stack.Count > 0;
        UndoButton.Content = stack.Count > 0
            ? $"Undo Last Command ({stack[0].Description})"
            : "Undo Last Command";
    }

    private void ShowAlerts(AlertsSnapshot? alerts)
    {
        if (alerts == null) return;

        ActiveErrorsText.Text = alerts.ActiveErrorStates.Count == 0
            ? "No active error states."
            : $"Active error states ({alerts.ActiveErrorStates.Count}, duplicates suppressed: {alerts.DuplicatesSuppressed}): " +
              string.Join("; ", alerts.ActiveErrorStates);

        AlertsList.ItemsSource = alerts.Recent
            .Select(a => $"{a.TimestampUtc.ToLocalTime():HH:mm:ss}  [{a.Severity}] {a.Message}")
            .ToList();
    }

    // ------------------------------------------------------------------ search + filters

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;

        // debounce: wait for a pause in typing before querying
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await LogSearchAsync();
        }
    }

    private async void SearchBox_LostFocus(object sender, RoutedEventArgs e) => await LogSearchAsync();

    // Reports a submitted search so the Action Engine can learn the user's patterns
    private async Task LogSearchAsync()
    {
        string term = SearchBox.Text.Trim();

        if (term.Length == 0 || term == _lastLoggedSearch)
        {
            return;
        }

        _lastLoggedSearch = term;

        try
        {
            await App.ApiClient.PostAsJsonAsync("/api/activity/search", new SearchActivityRequest { Term = term }, Json);
        }
        catch (Exception) { }
    }

    private async void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        await RefreshAsync();
    }

    private async void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        CategoryFilter.SelectedIndex = 0;
        StateFilter.SelectedIndex = 0;
        await RefreshAsync();
    }

    // ------------------------------------------------------------------ selection + timeline

    private async void DevicesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        if (DevicesGrid.SelectedItem is not DeviceState device) return;

        _selectedMac = device.MacAddress;
        _selectedDevice = device;
        UpdateSelectedHeader();

        await LoadTimelineAsync();
    }

    private async Task LoadTimelineAsync()
    {
        if (_selectedMac == null)
        {
            _timeline = new List<TelemetryRecord>();
            DrawTimeline();
            return;
        }

        try
        {
            // already in ascending timestamp order (SortedDictionary on the server)
            _timeline = await GetAsync<List<TelemetryRecord>>($"/api/devices/{Uri.EscapeDataString(_selectedMac)}/history?take=60")
                        ?? new List<TelemetryRecord>();
        }
        catch (Exception)
        {
            return;
        }

        DrawTimeline();
    }

    private void TimelineCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawTimeline();

    private static double ToNumber(TelemetryRecord record) =>
        ValueParser.TryParseNumber(record.DataValue, out double v) ? v : 0;

    // Draws the selected device's readings as a line chart on a Canvas (no external chart library)
    private void DrawTimeline()
    {
        TimelineCanvas.Children.Clear();

        double w = TimelineCanvas.ActualWidth;
        double h = TimelineCanvas.ActualHeight;

        TimelineTitle.Text = _selectedDevice == null
            ? "Telemetry Timeline"
            : $"Telemetry Timeline - {_selectedDevice.NodeId} ({_timeline.FirstOrDefault()?.PacketType})";

        if (w < 60 || h < 40) return;

        if (_timeline.Count < 2)
        {
            AddLabel(_selectedDevice == null ? "Select a device to see its timeline" : "Waiting for telemetry...", 12, h / 2 - 8, Brushes.Gray);
            return;
        }

        const double left = 42, right = 10, top = 10, bottom = 20;

        var values = _timeline.Select(ToNumber).ToList();
        double min = values.Min();
        double max = values.Max();

        if (Math.Abs(max - min) < 1e-9)
        {
            min -= 1;
            max += 1;
        }

        double plotW = w - left - right;
        double plotH = h - top - bottom;

        var line = new Polyline { Stroke = Brushes.SteelBlue, StrokeThickness = 2 };

        for (int i = 0; i < values.Count; i++)
        {
            double x = left + i * plotW / (values.Count - 1);
            double y = top + (max - values[i]) / (max - min) * plotH;

            line.Points.Add(new Point(x, y));

            // mark non-normal readings
            if (_timeline[i].Severity is "Critical" or "Warning")
            {
                var dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = _timeline[i].Severity == "Critical" ? Brushes.Red : Brushes.Orange
                };
                Canvas.SetLeft(dot, x - 4);
                Canvas.SetTop(dot, y - 4);
                TimelineCanvas.Children.Add(dot);
            }
        }

        // axes
        TimelineCanvas.Children.Add(new Line { X1 = left, Y1 = top, X2 = left, Y2 = top + plotH, Stroke = Brushes.Gray });
        TimelineCanvas.Children.Add(new Line { X1 = left, Y1 = top + plotH, X2 = left + plotW, Y2 = top + plotH, Stroke = Brushes.Gray });
        TimelineCanvas.Children.Add(line);

        AddLabel(max.ToString("0.#", CultureInfo.InvariantCulture), 2, top - 6, Brushes.DimGray);
        AddLabel(min.ToString("0.#", CultureInfo.InvariantCulture), 2, top + plotH - 8, Brushes.DimGray);
        AddLabel(_timeline[0].Timestamp.ToLocalTime().ToString("HH:mm:ss"), left, h - 16, Brushes.DimGray);
        AddLabel(_timeline[^1].Timestamp.ToLocalTime().ToString("HH:mm:ss"), w - 60, h - 16, Brushes.DimGray);
    }

    private void AddLabel(string text, double x, double y, Brush brush)
    {
        var label = new TextBlock { Text = text, FontSize = 10, Foreground = brush };
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        TimelineCanvas.Children.Add(label);
    }

    // ------------------------------------------------------------------ manual override commands

    private async void OverrideButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDevice == null) return;
        if (sender is not Button { Tag: string tag }) return;
        if (!Enum.TryParse<CommandType>(tag, out var type)) return;

        await IssueCommandAsync(_selectedDevice.MacAddress, type);
    }

    private async Task IssueCommandAsync(string mac, CommandType type)
    {
        try
        {
            var request = new CommandRequest
            {
                DeviceId = mac,
                Type = type,
                IssuedBy = Environment.UserName
            };

            using var response = await App.ApiClient.PostAsJsonAsync("/api/commands", request, Json);

            if (response.IsSuccessStatusCode)
            {
                var command = await response.Content.ReadFromJsonAsync<OverrideCommand>(Json);
                Feedback($"Sent: {command?.Description}", false);
            }
            else
            {
                Feedback($"Command failed ({(int)response.StatusCode}).", true);
            }
        }
        catch (Exception)
        {
            Feedback("Could not reach the API.", true);
        }

        await RefreshAsync();
    }

    private async void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var response = await App.ApiClient.PostAsync("/api/commands/undo", null);

            Feedback(response.IsSuccessStatusCode ? "Last command reverted." : "Nothing to undo.",
                     !response.IsSuccessStatusCode);
        }
        catch (Exception)
        {
            Feedback("Could not reach the API.", true);
        }

        await RefreshAsync();
    }

    private void Feedback(string message, bool isError)
    {
        FeedbackText.Text = message;
        FeedbackText.Foreground = isError ? Brushes.IndianRed : Brushes.ForestGreen;
    }

    // ------------------------------------------------------------------ suggestions

    private async void ApplySuggestion_Click(object sender, RoutedEventArgs e)
    {
        if (SuggestionsList.SelectedItem is not Suggestion suggestion)
        {
            Feedback("Select a suggestion first.", true);
            return;
        }

        switch (suggestion.Kind)
        {
            case "Command" when suggestion.DeviceId != null && suggestion.Command.HasValue:
                await IssueCommandAsync(suggestion.DeviceId, suggestion.Command.Value);
                break;

            case "Search":
                SearchBox.Text = suggestion.SearchTerm ?? string.Empty;
                await LogSearchAsync();
                await RefreshAsync();
                break;

            default:
                HighlightDevice(suggestion.DeviceId);
                break;
        }
    }

    private void SuggestionsList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SuggestionsList.SelectedItem is Suggestion { DeviceId: not null } suggestion)
        {
            HighlightDevice(suggestion.DeviceId);
        }
    }

    private void HighlightDevice(string? mac)
    {
        if (mac == null) return;

        _selectedMac = mac;

        var device = (DevicesGrid.ItemsSource as IEnumerable<DeviceState>)?
            .FirstOrDefault(d => d.MacAddress.Equals(mac, StringComparison.OrdinalIgnoreCase));

        if (device == null)
        {
            Feedback("That device is hidden by the current filter.", true);
            return;
        }

        DevicesGrid.SelectedItem = device;
        DevicesGrid.ScrollIntoView(device);
    }

    // ------------------------------------------------------------------ test / demo helpers

    private async void SimulateAlert_Click(object sender, RoutedEventArgs e)
    {
        // pick a packet type that fits the selected device's category
        SimulateAlertRequest? request = null;

        if (_selectedDevice != null)
        {
            request = new SimulateAlertRequest
            {
                DeviceId = _selectedDevice.MacAddress,
                PacketType = _selectedDevice.Category switch
                {
                    SensorCategory.Environmental => "Temperature",
                    SensorCategory.PowerConsumption => "PowerWattage",
                    _ => "Moisture"
                }
            };
        }

        try
        {
            using var response = await App.ApiClient.PostAsJsonAsync("/api/simulate/critical-alert", request ?? new SimulateAlertRequest(), Json);
            Feedback(response.IsSuccessStatusCode ? "Critical packet sent via the priority queue." : "Simulation failed.",
                     !response.IsSuccessStatusCode);
        }
        catch (Exception)
        {
            Feedback("Could not reach the API.", true);
        }

        await RefreshAsync();
    }

    private async void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        bool resume = PauseButton.Content?.ToString()?.StartsWith("Resume") == true;

        try
        {
            using var response = await App.ApiClient.PostAsync(resume ? "/api/pipeline/resume" : "/api/pipeline/pause", null);
        }
        catch (Exception)
        {
            Feedback("Could not reach the API.", true);
        }

        await RefreshAsync();
    }

    private async void SeedHistory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var response = await App.ApiClient.PostAsJsonAsync("/api/simulate/seed-history", new SeedHistoryRequest(), Json);
            Feedback(response.IsSuccessStatusCode ? "Action Engine trained with a sample pattern." : "Seeding failed.",
                     !response.IsSuccessStatusCode);
        }
        catch (Exception)
        {
            Feedback("Could not reach the API.", true);
        }

        await RefreshAsync();
    }
}
