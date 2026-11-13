using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SmartX.Shared.Models;

namespace SmartX.Wpf;

// One row of the historical telemetry grid
public class HistoryRow
{
    public string Time { get; set; } = string.Empty;
    public string PacketType { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
}

// Network Topology and Mesh Routing dashboard.
public partial class NetworkTopologyView : UserControl, INavigableView
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    private TopologySnapshot? _snapshot;
    private RouteResult? _route;
    private string? _selectedId;
    private string _nodeListKey = string.Empty;
    private bool _ready;
    private bool _refreshing;

    public NetworkTopologyView()
    {
        InitializeComponent();

        MstBox.ItemsSource = new[] { "Prim", "Kruskal" };
        MstBox.SelectedIndex = 0;

        TraversalBox.ItemsSource = new[] { "BFS", "DFS" };
        TraversalBox.SelectedIndex = 0;

        RangeBox.ItemsSource = new[] { "Last 30 minutes", "Last 1 hour", "Last 6 hours", "Custom (date + times)" };
        RangeBox.SelectedIndex = 2;

        TypeBox.ItemsSource = new[] { "All types", "Temperature", "PowerWattage", "Moisture", "ValveState", "RawBatch" };
        TypeBox.SelectedIndex = 0;

        CustomDate.SelectedDate = DateTime.Today;

        _refreshTimer.Tick += async (_, _) => await RefreshAsync();

        _ready = true;
    }

    // navigation

    public async Task OnNavigatedToAsync()
    {
        _refreshTimer.Start();
        await RefreshAsync();
    }

    // polling pauses on other tabs; the selection, results and history stay in this object
    public void OnNavigatedFrom() => _refreshTimer.Stop();

    public void Shutdown() => _refreshTimer.Stop();

    // helpers

    private string MstName() => MstBox.SelectedItem as string ?? "Prim";

    private static async Task<T?> GetAsync<T>(string url)
    {
        using var response = await App.ApiClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(Json);
    }

    private MeshNodeInfo? Selected() =>
        _selectedId == null ? null : _snapshot?.Nodes.FirstOrDefault(n => n.Id.Equals(_selectedId, StringComparison.OrdinalIgnoreCase));

    private void Feedback(string message, bool error)
    {
        FeedbackText.Text = message;
        FeedbackText.Foreground = error ? Brushes.IndianRed : Brushes.ForestGreen;
    }

    private void SetConnection(bool connected)
    {
        ConnectionText.Text = connected ? "● API connected" : "● API offline (retrying)";
        ConnectionText.Foreground = connected ? Brushes.ForestGreen : Brushes.IndianRed;
    }

    // refresh

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;

        try
        {
            string mst = MstName();

            var snapshot = await GetAsync<TopologySnapshot>($"/api/topology?mst={mst}");

            if (snapshot == null)
            {
                SetConnection(false);
                return;
            }

            _snapshot = snapshot;

            if (_selectedId != null)
            {
                _route = await GetAsync<RouteResult>($"/api/topology/nodes/{Uri.EscapeDataString(_selectedId)}/route?mst={mst}");
            }

            var failures = await GetAsync<List<FailureAdvice>>($"/api/topology/failures?mst={mst}") ?? new List<FailureAdvice>();

            SetConnection(true);
            UpdateNodeSelector();
            ShowSummary();
            ShowSelected();
            ShowRoute();
            ShowFailures(failures);
            DrawGraph();
        }
        catch (Exception)
        {
            // API restarting or offline: keep the last picture, never crash
            SetConnection(false);
        }
        finally
        {
            _refreshing = false;
        }
    }

    // fills the editable combo box with the node identifiers (only when the list changes, so typing is not disturbed)
    private void UpdateNodeSelector()
    {
        if (_snapshot == null) return;

        string key = string.Join("|", _snapshot.Nodes.Select(n => n.Label));

        if (key == _nodeListKey) return;

        _nodeListKey = key;

        string typed = NodeSelector.Text;
        NodeSelector.ItemsSource = _snapshot.Nodes.Select(n => n.Label).ToList();
        NodeSelector.Text = typed;
    }

    private void ShowSummary()
    {
        if (_snapshot == null) return;

        SummaryText.Text =
            $"{_snapshot.ReachableCount} of {_snapshot.NodeCount} nodes connected | {_snapshot.DownCount} down | " +
            $"{_snapshot.MstAlgorithm} MST: {_snapshot.MstEdgeCount} links, total power {_snapshot.MstTotalPowerCost:0.0}";
    }

    private void ShowSelected()
    {
        var node = Selected();

        if (node == null)
        {
            SelectedText.Text = "Click a node or enter a MAC address / ID above.";
            ToggleNodeButton.IsEnabled = false;
            HistoryNodeText.Text = "(none)";
            return;
        }

        var text = new StringBuilder();
        text.AppendLine($"{node.Label}  ({node.Role})");
        text.AppendLine($"MAC: {node.Id}");
        text.AppendLine($"Location: {node.Room}");
        text.AppendLine($"Status: {node.Status}");

        if (node.Reachable && node.Role != "Gateway")
        {
            text.AppendLine($"Hops to gateway: {node.HopsToGateway}");
            text.AppendLine($"Next hop: {node.NextHop}");
        }

        SelectedText.Text = text.ToString().TrimEnd();

        ToggleNodeButton.IsEnabled = true;
        ToggleNodeButton.Content = node.IsUp ? "Mark Node Down (simulate failure)" : "Restore Node";

        HistoryNodeText.Text = node.Role == "Gateway" ? "(gateway)" : node.Label;
    }

    private static string PathText(List<RouteHop> path) => string.Join(" → ", path.Select(h => h.Label));

    private void ShowRoute()
    {
        if (_route == null || Selected() == null)
        {
            RouteText.Text = "Select a node to see its route.";
            return;
        }

        var text = new StringBuilder();
        text.AppendLine(_route.Message);

        if (_route.Reachable)
        {
            text.AppendLine();
            text.AppendLine($"Most power-efficient ({_route.MstAlgorithm} spanning tree):");
            text.AppendLine(PathText(_route.MstPath));
            text.AppendLine($"{_route.MstHops} hop(s), power cost {_route.MstPowerCost:0.0}");
            text.AppendLine();
            text.AppendLine("Fewest hops (BFS):");
            text.AppendLine(PathText(_route.ShortestHopPath));
            text.Append($"{_route.ShortestHopCount} hop(s), power cost {_route.ShortestHopPowerCost:0.0}");
        }

        RouteText.Text = text.ToString();
    }

    private void ShowFailures(List<FailureAdvice> failures)
    {
        if (failures.Count == 0)
        {
            FailureText.Text = "No failed nodes. Use \"Mark Node Down\" to simulate a failure.";
            return;
        }

        var text = new StringBuilder();

        foreach (var failure in failures)
        {
            text.AppendLine($"{failure.DownNodeLabel} is down ({failure.Reason}).");

            if (failure.Affected.Count == 0)
            {
                text.AppendLine("  No other node relied on it.");
                continue;
            }

            foreach (var affected in failure.Affected)
            {
                text.AppendLine(affected.StillReachable
                    ? $"  {affected.NodeLabel}: {affected.Advice}  ({PathText(affected.NewPath)}, power {affected.OldPowerCost:0.0} → {affected.NewPowerCost:0.0})"
                    : $"  {affected.NodeLabel}: {affected.Advice}");
            }
        }

        FailureText.Text = text.ToString().TrimEnd();
    }

    // drawing the graph

    private void MeshCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawGraph();

    private static string PairKey(string a, string b) =>
        string.Compare(a, b, StringComparison.OrdinalIgnoreCase) < 0 ? $"{a}|{b}" : $"{b}|{a}";

    private void DrawGraph()
    {
        MeshCanvas.Children.Clear();

        if (_snapshot == null) return;

        double width = MeshCanvas.ActualWidth;
        double height = MeshCanvas.ActualHeight;

        if (width < 120 || height < 120) return;

        const double margin = 55;

        Point PositionOf(MeshNodeInfo node) =>
            new(margin + node.X * (width - 2 * margin), margin + node.Y * (height - 2 * margin));

        var nodes = _snapshot.Nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);

        // links on the tracked route get a blue halo
        var routeLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_route is { Reachable: true } && _route.MstPath.Count > 1)
        {
            for (int i = 0; i + 1 < _route.MstPath.Count; i++)
            {
                routeLinks.Add(PairKey(_route.MstPath[i].Id, _route.MstPath[i + 1].Id));
            }
        }

        // 1. route halo (under everything)
        foreach (var link in _snapshot.Links)
        {
            if (!routeLinks.Contains(PairKey(link.From, link.To))) continue;

            Point a = PositionOf(nodes[link.From]);
            Point b = PositionOf(nodes[link.To]);

            MeshCanvas.Children.Add(new Line
            {
                X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                Stroke = Brushes.DodgerBlue,
                StrokeThickness = 11,
                Opacity = 0.35
            });
        }

        // 2. links
        foreach (var link in _snapshot.Links)
        {
            Point a = PositionOf(nodes[link.From]);
            Point b = PositionOf(nodes[link.To]);

            var line = new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y };

            if (!link.IsUp)
            {
                line.Stroke = Brushes.IndianRed;
                line.StrokeThickness = 1.5;
                line.StrokeDashArray = new DoubleCollection { 4, 3 };
            }
            else if (link.InMst)
            {
                line.Stroke = Brushes.ForestGreen;
                line.StrokeThickness = 3.5;
            }
            else
            {
                line.Stroke = Brushes.LightSlateGray;
                line.StrokeThickness = 1.5;
            }

            MeshCanvas.Children.Add(line);

            var cost = new TextBlock
            {
                Text = link.PowerCost.ToString("0.0", CultureInfo.InvariantCulture),
                FontSize = 10,
                Background = Brushes.White,
                Foreground = Brushes.DimGray
            };

            Canvas.SetLeft(cost, (a.X + b.X) / 2 - 8);
            Canvas.SetTop(cost, (a.Y + b.Y) / 2 - 8);
            MeshCanvas.Children.Add(cost);
        }

        // 3. nodes
        foreach (var node in _snapshot.Nodes)
        {
            Point p = PositionOf(node);
            bool isSelected = node.Id.Equals(_selectedId, StringComparison.OrdinalIgnoreCase);
            bool isGateway = node.Role == "Gateway";

            Brush fill = isGateway ? new SolidColorBrush(Color.FromRgb(0x1F, 0x2A, 0x3C))
                : !node.IsUp ? Brushes.IndianRed
                : node.Reachable ? Brushes.ForestGreen
                : Brushes.Orange;

            double size = isGateway ? 44 : 36;

            Shape shape = isGateway ? new Rectangle() : new Ellipse();
            shape.Width = size;
            shape.Height = size;
            shape.Fill = fill;
            shape.Stroke = isSelected ? Brushes.Black : Brushes.White;
            shape.StrokeThickness = isSelected ? 4 : 2;
            shape.Cursor = Cursors.Hand;
            shape.ToolTip = $"{node.Label}\n{node.Id}\n{node.Room}\nStatus: {node.Status}";

            string id = node.Id;
            shape.MouseLeftButtonDown += async (_, _) => await SelectNodeAsync(id);

            Canvas.SetLeft(shape, p.X - size / 2);
            Canvas.SetTop(shape, p.Y - size / 2);
            MeshCanvas.Children.Add(shape);

            var label = new TextBlock
            {
                Text = node.Label,
                FontSize = 11,
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(label, p.X - 26);
            Canvas.SetTop(label, p.Y + size / 2 + 2);
            MeshCanvas.Children.Add(label);
        }
    }

    // selecting + tracking nodes

    private async Task SelectNodeAsync(string id)
    {
        _selectedId = id;

        var node = Selected();

        if (node != null)
        {
            NodeSelector.Text = node.Label;
        }

        FeedbackText.Text = string.Empty;

        // show the selection immediately; the refresh below loads the route
        ShowSelected();
        DrawGraph();

        await RefreshAsync();
    }

    // accepts a MAC address or an identifier such as NODE-02
    private MeshNodeInfo? ResolveNode(string? text)
    {
        if (_snapshot == null || string.IsNullOrWhiteSpace(text)) return null;

        string wanted = text.Trim();

        return _snapshot.Nodes.FirstOrDefault(n =>
            n.Id.Equals(wanted, StringComparison.OrdinalIgnoreCase) ||
            n.Label.Equals(wanted, StringComparison.OrdinalIgnoreCase));
    }

    private async Task TrackTypedNodeAsync()
    {
        var node = ResolveNode(NodeSelector.Text);

        if (node == null)
        {
            Feedback("No node with that MAC address or identifier.", true);
            return;
        }

        await SelectNodeAsync(node.Id);
    }

    private async void TrackRoute_Click(object sender, RoutedEventArgs e) => await TrackTypedNodeAsync();

    private async void NodeSelector_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await TrackTypedNodeAsync();
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void Algorithm_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        await RefreshAsync();
    }

    // failures

    private async void ToggleNode_Click(object sender, RoutedEventArgs e)
    {
        var node = Selected();

        if (node == null)
        {
            Feedback("Select a node first.", true);
            return;
        }

        try
        {
            var request = new NodeStatusRequest { IsUp = !node.IsUp };

            using var response = await App.ApiClient.PostAsJsonAsync(
                $"/api/topology/nodes/{Uri.EscapeDataString(node.Id)}/status", request, Json);

            Feedback(response.IsSuccessStatusCode
                ? (request.IsUp ? $"{node.Label} restored." : $"{node.Label} marked down. Routes recalculated.")
                : "Could not change the node status.",
                !response.IsSuccessStatusCode);
        }
        catch (Exception)
        {
            Feedback("Could not reach the API.", true);
        }

        await RefreshAsync();
    }

    // BFS / DFS

    private async void RunTraversal_Click(object sender, RoutedEventArgs e)
    {
        string algorithm = (TraversalBox.SelectedItem as string ?? "BFS").ToLowerInvariant();
        string root = _selectedId ?? _snapshot?.GatewayId ?? string.Empty;

        try
        {
            var result = await GetAsync<TraversalResult>(
                $"/api/topology/traverse?from={Uri.EscapeDataString(root)}&algorithm={algorithm}");

            if (result == null)
            {
                TraversalSummary.Text = "Traversal failed.";
                return;
            }

            TraversalSummary.Text =
                $"{result.Algorithm} from {result.RootLabel}: {result.Connected.Count} connected, " +
                $"{result.Unreachable.Count} down or isolated.";

            var lines = new List<string>();
            int step = 1;

            foreach (var entry in result.Connected)
            {
                string via = entry.ParentLabel == null ? "start" : $"via {entry.ParentLabel}";
                lines.Add($"{step++,2}. {entry.Label,-8} hops {entry.Hops}  {via}");
            }

            foreach (var entry in result.Unreachable)
            {
                lines.Add($" ✗  {entry.Label,-8} {entry.Reason}");
            }

            TraversalList.ItemsSource = lines;
        }
        catch (Exception)
        {
            TraversalSummary.Text = "Could not reach the API.";
        }
    }

    // historical log (AVL tree)

    private void RangeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;

        bool custom = (RangeBox.SelectedItem as string)?.StartsWith("Custom") == true;

        CustomDate.IsEnabled = custom;
        FromTime.IsEnabled = custom;
        ToTime.IsEnabled = custom;
    }

    // returns the local start/end of the chosen range, or null when the custom times are invalid
    private (DateTime From, DateTime To)? GetRange()
    {
        DateTime now = DateTime.Now;

        switch (RangeBox.SelectedItem as string)
        {
            case "Last 30 minutes":
                return (now.AddMinutes(-30), now);

            case "Last 1 hour":
                return (now.AddHours(-1), now);

            case "Last 6 hours":
                return (now.AddHours(-6), now);
        }

        DateTime day = (CustomDate.SelectedDate ?? DateTime.Today).Date;

        if (!TimeSpan.TryParse(FromTime.Text.Trim(), CultureInfo.InvariantCulture, out TimeSpan from) ||
            !TimeSpan.TryParse(ToTime.Text.Trim(), CultureInfo.InvariantCulture, out TimeSpan to))
        {
            StatsText.Text = "Enter the custom times as HH:mm, for example 14:00.";
            return null;
        }

        // the "to" time includes that whole minute
        DateTime start = day + from;
        DateTime end = day + to + TimeSpan.FromSeconds(59);

        if (end < start)
        {
            StatsText.Text = "The end time must be after the start time.";
            return null;
        }

        return (start, end);
    }

    private async void QueryHistory_Click(object sender, RoutedEventArgs e) => await QueryHistoryAsync();

    private async Task QueryHistoryAsync()
    {
        var node = Selected();

        if (node == null || node.Role == "Gateway")
        {
            StatsText.Text = "Select a sensor node first (click a node or enter its MAC address / ID).";
            return;
        }

        var range = GetRange();

        if (range == null) return;

        string from = range.Value.From.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        string to = range.Value.To.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

        string url = $"/api/history/{Uri.EscapeDataString(node.Id)}?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}&take=500";

        if (TypeBox.SelectedIndex > 0 && TypeBox.SelectedItem is string type)
        {
            url += "&type=" + Uri.EscapeDataString(type);
        }

        try
        {
            var result = await GetAsync<RangeQueryResult>(url);

            if (result == null)
            {
                StatsText.Text = "The query failed.";
                return;
            }

            HistoryGrid.ItemsSource = result.Records
                .Select(r => new HistoryRow
                {
                    Time = r.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    PacketType = r.PacketType,
                    Value = r.DataValue,
                    Severity = r.Severity
                })
                .ToList();

            StatsText.Text = result.TotalRecordsInTree == 0
                ? $"{node.Label} has no stored history yet. Press \"Load Demo History\" or start the telemetry simulation."
                : $"{result.TotalMatches} match(es), {result.Returned} shown | AVL tree visited {result.NodesVisited} of " +
                  $"{result.TotalNodesInTree} nodes | height {result.TreeHeight} (optimal {result.OptimalHeight}) | " +
                  $"{result.ElapsedMicroseconds:0} µs";
        }
        catch (Exception)
        {
            StatsText.Text = "Could not reach the API.";
        }
    }

    private async void SeedHistory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var response = await App.ApiClient.PostAsync("/api/history/seed?hours=6", null);

            StatsText.Text = response.IsSuccessStatusCode
                ? "Demo history loaded (6 hours for every node). Select a node and press Query History."
                : "Could not load the demo history.";
        }
        catch (Exception)
        {
            StatsText.Text = "Could not reach the API.";
        }

        if (Selected() is { Role: not "Gateway" })
        {
            await QueryHistoryAsync();
        }
    }
}
