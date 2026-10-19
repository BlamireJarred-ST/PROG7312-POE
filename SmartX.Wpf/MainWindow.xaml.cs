using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SmartX.Wpf
{
    // left navigation menu and content host that shows one module view at a time.
    public partial class MainWindow : Window
    {
        // Each module view is created once and cached, so switching modules never loses state
        private readonly Dictionary<string, UserControl> _views = new();

        private UserControl? _current;

        // Periodically checks that the API backend is reachable
        private readonly DispatcherTimer _healthTimer = new() { Interval = TimeSpan.FromSeconds(5) };
        private bool _pinging;

        public MainWindow()
        {
            InitializeComponent();

            _healthTimer.Tick += async (_, _) => await PingApiAsync();
            _healthTimer.Start();
            _ = PingApiAsync();

            Closed += (_, _) =>
            {
                _healthTimer.Stop();

                foreach (var view in _views.Values)
                {
                    if (view is INavigableView navigable)
                    {
                        navigable.Shutdown();
                    }
                }
            };

            // Opens the first module
            SensorsNav.IsChecked = true;
        }

        // Creates a module view the first time it is needed
        private UserControl GetOrCreateView(string key)
        {
            if (!_views.TryGetValue(key, out var view))
            {
                view = key switch
                {
                    "Sensors" => new SensorDashboard(),
                    "Commands" => new CommandStreamView(),
                    "Topology" => new NetworkTopologyView(),
                    _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown module")
                };

                _views[key] = view;
            }

            return view;
        }

        private async void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton { Tag: string key })
            {
                await NavigateAsync(key);
            }
        }

        // swap the view instantly, then let the view refresh its data without blocking the UI thread.
        private async Task NavigateAsync(string key)
        {
            var next = GetOrCreateView(key);

            if (ReferenceEquals(next, _current))
            {
                return;
            }

            if (_current is INavigableView previous)
            {
                previous.OnNavigatedFrom();
            }

            _current = next;
            ContentHost.Content = next;

            try
            {
                if (next is INavigableView navigable)
                {
                    await navigable.OnNavigatedToAsync();
                }
            }
            catch (Exception ex)
            {
                // A failing module doesnt take the shell down
                ApiStatusText.Text = $"● {ex.GetType().Name} while opening module";
                ApiStatusText.Foreground = Brushes.Orange;
            }
        }

        private async Task PingApiAsync()
        {
            if (_pinging) return;
            _pinging = true;

            try
            {
                using var response = await App.ApiClient.GetAsync("/api/pipeline/status");

                bool ok = response.IsSuccessStatusCode;
                ApiStatusText.Text = ok ? "● API connected" : "● API error";
                ApiStatusText.Foreground = ok ? Brushes.LightGreen : Brushes.Orange;
            }
            catch (Exception)
            {
                ApiStatusText.Text = "● API offline";
                ApiStatusText.Foreground = Brushes.IndianRed;
            }
            finally
            {
                _pinging = false;
            }
        }
    }
}
