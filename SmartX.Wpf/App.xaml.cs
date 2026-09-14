using System.Net.Http;
using System.Windows;

namespace SmartX.Wpf
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static readonly HttpClient ApiClient = new HttpClient
        {
            BaseAddress = new Uri("https://localhost:7078")
        };
    }

}
