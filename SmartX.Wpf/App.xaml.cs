
using System;
using System.Net.Http;
using System.Windows;

namespace SmartX.Wpf
{
    public partial class App : Application
    {
        public static readonly HttpClient ApiClient = new HttpClient
        {
            BaseAddress = new Uri("https://localhost:7078")
        };

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var loginWindow = new LoginWindow();
            MainWindow = loginWindow;
            loginWindow.Show();
        }
    }
}
