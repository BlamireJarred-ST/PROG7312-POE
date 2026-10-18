using System.Windows;

namespace SmartX.Wpf
{
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