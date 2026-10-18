namespace SmartX.Wpf;

// Implemented by every module view hosted in the main window.
// Views are created once and kept alive, so navigating away never loses their state.
public interface INavigableView
{
    // Called each time the user navigates to the view (start polling, refresh data...)
    Task OnNavigatedToAsync();

    // Called when the user navigates to another module (pause polling; keep all state)
    void OnNavigatedFrom();

    // Called once when the application closes
    void Shutdown();
}
