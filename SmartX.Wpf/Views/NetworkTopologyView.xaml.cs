using System.Windows.Controls;

namespace SmartX.Wpf;

// Placeholder for the final PoE module
public partial class NetworkTopologyView : UserControl, INavigableView
{
    public NetworkTopologyView()
    {
        InitializeComponent();
    }

    public Task OnNavigatedToAsync() => Task.CompletedTask;

    public void OnNavigatedFrom() { }

    public void Shutdown() { }
}
