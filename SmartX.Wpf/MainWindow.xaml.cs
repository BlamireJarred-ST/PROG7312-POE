using System.Windows;

namespace SmartX.Wpf
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void SensorPillarButton_Click(object sender, RoutedEventArgs e)
        {
            var dashboard = new SensorDashboard();

            dashboard.ShowDialog();
        }
    }
}