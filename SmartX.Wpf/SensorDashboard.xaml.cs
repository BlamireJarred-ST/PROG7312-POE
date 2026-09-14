
using Microsoft.Win32;
using SmartX.Shared.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;

namespace SmartX.Wpf;

// Handles the sensor dashboard UI, including telemetry simulation, sensor registration, and engagement features.
public partial class SensorDashboard : Window
{
    // Storage for telemetry records displayed in the DataGrid.
    private readonly ObservableCollection<TelemetryRecord> _telemetryRecords = new();
    // Timer for simulated telemetry data
    private DispatcherTimer? _simulationTimer;
    // Random number generator for simulating telemetry data
    private readonly Random _random = new();
    // Indicates whether the telemetry simulation is currently running
    private bool _isSimulating = false;
    // Stops telemetry data being processed at the same time
    private bool _isBusy = false;

    //Health monitoring and engagement
    private int _healthScore = 100;
    private int _acknowledgeCount = 0;
    private readonly List<string> _pendingAlerts = new();
    private readonly HashSet<string> _earnedBadges = new();
    private readonly SmartMeterReading _powerWarningThreshold = new(700);
    private readonly SmartMeterReading _powerCriticalThreshold = new(900);
    private readonly double _tempWarningThreshold = 35.0;
    private readonly double _tempCriticalThreshold = 45.0;

    // Sensor information
    private List<string> _registeredMacs = new();
    private List<Sensor> _loadedSensors = new();

    // Retrive sensors from SmartX.API
    private async Task LoadRegisteredSensorsAsync()
    {
        try
        {
            var response = await App.ApiClient.GetAsync("/api/sensors");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions
                { PropertyNameCaseInsensitive = true };
                var sensors = JsonSerializer
                    .Deserialize<List<Sensor>>(json, options);

                if (sensors != null)
                {
                    // Store the loaded sensors and their MAC addresses for simulation
                    _loadedSensors = sensors;
                    _registeredMacs = sensors
                        .Select(s => s.MacAddress)
                        .ToList();

                    // Populate the node selector ComboBox
                    Dispatcher.Invoke(() =>
                    {
                        SimulationNodeSelector.Items.Clear();

                        foreach (var sensor in sensors)
                        {
                            SimulationNodeSelector.Items.Add(
                                $"{sensor.MacAddress} ({sensor.NodeId})");
                        }

                        // Select the first item by default if available
                        if (SimulationNodeSelector.Items.Count > 0)
                            SimulationNodeSelector.SelectedIndex = 0;
                    });
                }
            }
        }
        catch (HttpRequestException) { } // Dashboard will continue to function without the API connection
    }

    // Initializes the dashboard, sets up the telemetry DataGrid, and loads registered sensors.
    public SensorDashboard()
    {
        InitializeComponent();
        TelemetryGrid.ItemsSource = _telemetryRecords;
        _ = LoadRegisteredSensorsAsync();
    }

    // Handles the registration of a new sensor when the "Register" button is clicked.
    private async void RegisterButton_Click(object sender, RoutedEventArgs e)
    {
        // Read values from the form
        string mac = MacAddressInput.Text.Trim();
        string room = RoomInput.Text.Trim();
        string zone = ZoneInput.Text.Trim();
        string node = NodeIdInput.Text.Trim();

        // Validate MAC address
        if (string.IsNullOrWhiteSpace(mac))
        {
            MessageBox.Show(
                "Please enter a MAC address.",
                "Validation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        // Validate MAC address format
        if (mac.Length != 17 || !mac.Contains(':'))
        {
            MessageBox.Show(
                "Please enter a valid MAC address.\n\nExample: AA:BB:CC:DD:EE:FF",
                "Validation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }


        // Validate Room
        if (string.IsNullOrWhiteSpace(room))
        {
            MessageBox.Show(
                "Please enter a room.",
                "Validation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }


        // Validate Zone
        if (string.IsNullOrWhiteSpace(zone))
        {
            MessageBox.Show(
                "Please enter a zone.",
                "Validation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }


        // Validate Node ID
        if (string.IsNullOrWhiteSpace(node))
        {
            MessageBox.Show(
                "Please enter a Node ID.",
                "Validation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }


        // Convert ComboBox selection to SensorCategory
        var category = (SensorCategory)CategoryInput.SelectedIndex;


        // Build Sensor object
        var sensor = new Sensor
        {
            MacAddress = mac,
            Room = room,
            Zone = zone,
            NodeId = node,
            Category = category
        };


        try
        {
            // Convert Sensor to JSON
            var json = JsonSerializer.Serialize(sensor);

            // Create HTTP request content
            var content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json");


            // Send sensor to API
            var response = await App.ApiClient.PostAsync(
                "/api/sensors",
                content);


            // Successful registration
            if (response.IsSuccessStatusCode)
            {
                MessageBox.Show(
                    "Sensor registered successfully!",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                ClearForm();
                await LoadRegisteredSensorsAsync();
            }
            else
            {
                // Read API error
                var error = await response.Content.ReadAsStringAsync();

                MessageBox.Show(
                    $"The sensor could not be registered.\n\n" +
                    $"Status: {(int)response.StatusCode} {response.StatusCode}\n\n" +
                    $"Response:\n{error}",
                    "Registration Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        // Handle connection issues with the API
        catch (HttpRequestException)
        {
            MessageBox.Show(
                "Could not connect to the SmartX API.\n\n" +
                "Please make sure the API is running and that the API URL in App.xaml.cs is correct.",
                "Connection Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // Handles the start/stop of telemetry simulation when the "Start Simulation" button is clicked.
    private void StartSimulationButton_Click(object sender, RoutedEventArgs e)
    {
        // If simulation is already running, stop it
        if (_isSimulating)
        {
            _simulationTimer?.Stop();

            _isSimulating = false;

            // Update button text to indicate simulation can be started again
            StartSimulationButton.Content = "Start Simulation";

            return;
        }

        // Timer setup for generating and sending telemetry data every 2 seconds
        _simulationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };

        //Call the SimulationTimer_Tick method every time the timer ticks
        _simulationTimer.Tick += SimulationTimer_Tick;
        // Start generating data
        _simulationTimer.Start();

        _isSimulating = true;

        // Update button text to indicate simulation can be stopped
        StartSimulationButton.Content = "Stop Simulation";
    }

    // Determines the packet type based on the sensor's category for a given MAC address.
    private string GetPacketTypeForMac(string mac)
    {
        // Find the sensor in the loaded list
        var sensor = _loadedSensors
            .FirstOrDefault(s => s.MacAddress.Equals(
                mac, StringComparison.OrdinalIgnoreCase));

        if (sensor == null)
            return "Temperature";  // default

        // Convert sensor category 
        return sensor.Category switch
        {
            SensorCategory.Environmental => "Temperature",      // Environmental sensors report temperature
            SensorCategory.PowerConsumption => "PowerWattage",  // Power consumption sensors report wattage
            SensorCategory.Actuator => "ValveState",            // Actuator sensors report valve state
            _ => "Temperature"                                  // default 
        };
    }

    private async void SimulationTimer_Tick(object? sender, EventArgs e)
    {
        // Don't start another request if the previous telemetry request is still running.
        if (_isBusy) return;

        _isBusy = true;

        try
        {
            // GENERATE
            if (_registeredMacs.Count == 0)
            {
                await LoadRegisteredSensorsAsync();
                if (_registeredMacs.Count == 0) return;
            }

            string mac;

            if (SimulationNodeSelector.SelectedItem != null)
            {
                // Extract MAC from "AA:BB:CC:DD:EE:FF (NODE-01)"
                string selected = SimulationNodeSelector.SelectedItem.ToString()!;
                mac = selected.Split(' ')[0];
            }
            else if (_registeredMacs.Count > 0)
            {
                mac = _registeredMacs[0];  // fallback
            }
            else
            {
                return;
            }

            string packetType = GetPacketTypeForMac(mac);

            TelemetryRecord record;

            switch (packetType)
            {
                case "Temperature":
                    {
                        float temperature = (float)(_random.NextDouble() * 50);

                        var packet = new TelemetryPacket<float>(mac, temperature, "Temperature");

                        record = new TelemetryRecord
                        {
                            DeviceId = packet.DeviceId,
                            Timestamp = packet.Timestamp,
                            PacketType = packet.PacketType,
                            DataValue = packet.Data.ToString("F2"),
                            Severity = "Normal"
                        };
                        break;
                    }

                case "PowerWattage":
                    {
                        int watts = _random.Next(0, 1000);

                        var packet = new TelemetryPacket<int>(mac, watts, "PowerWattage");

                        record = new TelemetryRecord
                        {
                            DeviceId = packet.DeviceId,
                            Timestamp = packet.Timestamp,
                            PacketType = packet.PacketType,
                            DataValue = packet.Data.ToString(),
                            Severity = "Normal"
                        };
                        break;
                    }

                default: // ValveState
                    {
                        bool valve = _random.Next(2) == 1;

                        var packet = new TelemetryPacket<bool>(mac, valve, "ValveState");

                        record = new TelemetryRecord
                        {
                            DeviceId = packet.DeviceId,
                            Timestamp = packet.Timestamp,
                            PacketType = packet.PacketType,
                            DataValue = packet.Data.ToString(),
                            Severity = "Normal"
                        };
                        break;
                    }
            }

            record.Severity = DetermineSeverity(record);

            if (record.Severity != "Normal")
            {
                RegisterAnomaly(record);
            }

            // SEND

            string json = JsonSerializer.Serialize(record);

            using var content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json");

            var postResponse =
                await App.ApiClient.PostAsync(
                    "/api/telemetry",
                    content);

            if (!postResponse.IsSuccessStatusCode)
            {
                return;
            }

            // FETCH

            var getResponse =
                await App.ApiClient.GetAsync("/api/telemetry");

            if (!getResponse.IsSuccessStatusCode)
            {
                return;
            }

            string responseJson =
                await getResponse.Content.ReadAsStringAsync();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var records = JsonSerializer.Deserialize<List<TelemetryRecord>>(responseJson, options);

            if (records == null)
            {
                return;
            }

            // UPDATE DATAGRID

            _telemetryRecords.Clear();

            foreach (var telemetryRecord in records)
            {
                _telemetryRecords.Add(telemetryRecord);
            }
        }
        catch (HttpRequestException) { }
        catch (Exception) { }
        finally
        {
            _isBusy = false;
        }
    }

    private async void AttachFileButton_Click(object sender, RoutedEventArgs e)
    {
        string mac = MacAddressInput.Text.Trim();

        // Make sure a sensor MAC address was provided.
        if (string.IsNullOrWhiteSpace(mac))
        {
            MessageBox.Show(
                "Please enter the MAC address of the sensor you want to attach a file to.",
                "MAC Address Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        // Open the file picker.
        var dialog = new OpenFileDialog
        {
            Title = "Select a config file, photo, or log",
            Filter = "All Files (*.*)|*.*|" +
                     "Images|*.jpg;*.jpeg;*.png|" +
                     "Logs|*.log;*.txt|" +
                     "Config|*.json;*.xml"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            string filePath = dialog.FileName;
            string fileName = Path.GetFileName(filePath);

            // Read the file.
            byte[] fileBytes = await File.ReadAllBytesAsync(filePath);

            // Limit uploads to 10 MB.
            const long maxBytes = 10 * 1024 * 1024;

            if (fileBytes.Length > maxBytes)
            {
                MessageBox.Show(
                    "File must be under 10MB.",
                    "File Too Large",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            // Create multipart form data.
            using var multipart = new MultipartFormDataContent();

            using var fileContent = new ByteArrayContent(fileBytes);

            fileContent.Headers.ContentType =
                new MediaTypeHeaderValue("application/octet-stream");

            // IMPORTANT:
            // "file" must match the IFormFile parameter name
            // in the API endpoint.
            multipart.Add(
                fileContent,
                "file",
                fileName);

            // Upload to the API.
            var response = await App.ApiClient.PostAsync(
                $"/api/sensors/{Uri.EscapeDataString(mac)}/attachments",
                multipart);

            if (response.IsSuccessStatusCode)
            {
                MessageBox.Show(
                    $"File attached successfully.\n\n" +
                    $"File: {fileName}\n" +
                    $"Size: {fileBytes.Length:N0} bytes",
                    "Attachment Uploaded",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            // Sensor doesn't exist.
            if (response.StatusCode ==
                System.Net.HttpStatusCode.NotFound)
            {
                MessageBox.Show(
                    "No sensor with that MAC address is registered. " +
                    "Please register the sensor first.",
                    "Sensor Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            // Other API failure.
            string responseBody =
                await response.Content.ReadAsStringAsync();

            MessageBox.Show(
                $"Upload failed.\n\n" +
                $"Status: {(int)response.StatusCode} " +
                $"{response.StatusCode}\n\n" +
                $"Response: {responseBody}",
                "Upload Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (HttpRequestException)
        {
            MessageBox.Show(
                "Could not connect to the SmartX API. " +
                "Please make sure the API is running.",
                "API Connection Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"An unexpected error occurred:\n\n{ex.Message}",
                "Upload Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private string DetermineSeverity(TelemetryRecord record)
    {
        switch (record.PacketType)
        {
            case "Temperature":
                {
                    if (!double.TryParse(
                            record.DataValue,
                            out double temperature))
                    {
                        return "Normal";
                    }

                    if (temperature > _tempCriticalThreshold)
                    {
                        return "Critical";
                    }

                    if (temperature > _tempWarningThreshold)
                    {
                        return "Warning";
                    }

                    return "Normal";
                }

            case "PowerWattage":
                {
                    if (!double.TryParse(
                            record.DataValue,
                            out double value))
                    {
                        return "Normal";
                    }

                    var reading = new SmartMeterReading(value);

                    if (reading > _powerCriticalThreshold)
                    {
                        return "Critical";
                    }

                    if (reading > _powerWarningThreshold)
                    {
                        return "Warning";
                    }

                    return "Normal";
                }

            case "ValveState":
                {
                    if (!bool.TryParse(
                            record.DataValue,
                            out bool valveOpen))
                    {
                        return "Normal";
                    }

                    // A valve that is OPEN is considered unexpected
                    // and therefore generates a warning.
                    if (valveOpen)
                    {
                        return "Warning";
                    }

                    return "Normal";
                }

            default:
                return "Normal";
        }
    }

    private void RegisterAnomaly(TelemetryRecord record)
    {
        // Reduce health by 5 points.
        // Health can never go below zero.
        _healthScore = Math.Max(0, _healthScore - 5);

        // Create an alert message.
        string alert =
            $"[{record.PacketType}] {record.DataValue} on " +
            $"{record.DeviceId} @ {record.Timestamp:HH:mm:ss}";

        _pendingAlerts.Add(alert);

        // Refresh the engagement UI.
        UpdateEngagementUI();
    }

    private void UpdateEngagementUI()
    {
        // Update health score.
        HealthScoreText.Text = _healthScore.ToString();

        // Change health score colour.
        if (_healthScore >= 80)
        {
            HealthScoreText.Foreground = Brushes.Green;
        }
        else if (_healthScore >= 50)
        {
            HealthScoreText.Foreground = Brushes.Orange;
        }
        else
        {
            HealthScoreText.Foreground = Brushes.Red;
        }

        // Refresh the pending alerts list.
        AlertsListBox.ItemsSource = _pendingAlerts.ToList();

        // Enable acknowledgement only when alerts exist.
        AcknowledgeButton.IsEnabled = _pendingAlerts.Count > 0;

        // Check whether a new badge has been earned.
        CheckAndAwardBadges();

        // Display earned badges.
        if (_earnedBadges.Count == 0)
        {
            BadgeText.Text = "No badges earned yet";
        }
        else
        {
            BadgeText.Text = string.Join(", ", _earnedBadges);
        }
    }

    private void CheckAndAwardBadges()
    {
        if (_acknowledgeCount >= 5 &&
            !_earnedBadges.Contains("Anomaly Hunter"))
        {
            _earnedBadges.Add("Anomaly Hunter");

            MessageBox.Show(
                "Badge Earned: Anomaly Hunter!",
                "Badge Earned",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        if (_acknowledgeCount >= 10 &&
            !_earnedBadges.Contains("System Guardian"))
        {
            _earnedBadges.Add("System Guardian");

            MessageBox.Show(
                "Badge Earned: System Guardian!",
                "Badge Earned",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

private void AcknowledgeButton_Click(
    object sender,
    RoutedEventArgs e)
    {
        // Nothing to acknowledge.
        if (_pendingAlerts.Count == 0)
        {
            return;
        }

        // Remove the oldest alert.
        _pendingAlerts.RemoveAt(0);

        // Restore 2 health points.
        // Health can never exceed 100.
        _healthScore = Math.Min(100, _healthScore + 2);

        // Increase acknowledgement count.
        _acknowledgeCount++;

        // Refresh the UI.
        UpdateEngagementUI();
    }
    
    protected override void OnClosed(EventArgs e)
    {
        _simulationTimer?.Stop();

        base.OnClosed(e);
    }


    private void ClearForm()
    {
        MacAddressInput.Text = string.Empty;
        RoomInput.Text = string.Empty;
        ZoneInput.Text = string.Empty;
        NodeIdInput.Text = string.Empty;

        CategoryInput.SelectedIndex = 0;
    }
}