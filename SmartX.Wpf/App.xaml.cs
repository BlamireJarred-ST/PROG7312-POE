using System.Net.Http;
using System.Windows;

namespace SmartX.Wpf
{
    // Interaction logic for App.xaml
    public partial class App : Application
    {
        // Shared HTTP vlient for the WPF application
        public static readonly HttpClient ApiClient = new HttpClient
        {
            // Base adress of SmartX API 
            BaseAddress = new Uri(Environment.GetEnvironmentVariable("SMARTX_API_URL") ?? "https://localhost:7078")
        };
    }

}
