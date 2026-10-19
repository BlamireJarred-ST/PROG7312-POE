
using System;
using System.Net.Http.Headers;
using System.Windows;

namespace SmartX.Wpf;

public partial class LoginWindow : Window
{
    private readonly FirebaseAuthService _authService = new();

    public LoginWindow()
    {
        InitializeComponent();
    }

    private async void LoginButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var email = EmailInput.Text.Trim();
        var password = PasswordInput.Password;

        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
        {
            StatusText.Text = "Enter your email and password.";
            return;
        }

        LoginButton.IsEnabled = false;
        LoginButton.Content = "Signing in...";
        StatusText.Text = "";

        try
        {
            var idToken = await _authService.SignInAsync(email, password);

            // Attach the Firebase token to future SmartX API requests.
            App.ApiClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", idToken);

            var mainWindow = new MainWindow();

            Application.Current.MainWindow = mainWindow;
            mainWindow.Show();
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            LoginButton.IsEnabled = true;
            LoginButton.Content = "Sign in";
        }
    }
}
