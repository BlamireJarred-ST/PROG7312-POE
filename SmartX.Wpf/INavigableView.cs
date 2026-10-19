namespace SmartX.Wpf;

// Views are created once and kept alive, so navigating away never loses their state.
public interface INavigableView
{
    // Called each time the user navigates to the view 
    Task OnNavigatedToAsync();

    // Called when the user navigates to another module
    void OnNavigatedFrom();

    // Called once when the application closes
    void Shutdown();
}
