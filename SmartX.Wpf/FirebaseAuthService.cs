
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace SmartX.Wpf;

public sealed class FirebaseAuthService
{
    private const string ApiKey = "AIzaSyAWgHmMZMn_Wwuyily7g7pLGziv72Ecq9s";

    private static readonly HttpClient Http = new();

    public async Task<string> SignInAsync(
        string email,
        string password)
    {
        var url =
            "https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key="
            + ApiKey;

        var request = new
        {
            email,
            password,
            returnSecureToken = true
        };

        using var response = await Http.PostAsJsonAsync(url, request);
        var json = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!response.IsSuccessStatusCode)
        {
            var message = "Login failed. Check your email and password.";

            if (root.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var detail))
            {
                message = detail.GetString() switch
                {
                    "EMAIL_NOT_FOUND" => "No account exists with that email.",
                    "INVALID_PASSWORD" => "The password is incorrect.",
                    "INVALID_LOGIN_CREDENTIALS" =>
                        "The email or password is incorrect.",
                    "USER_DISABLED" => "This account has been disabled.",
                    _ => "Login failed. Please check your details and try again."
                };
            }

            throw new InvalidOperationException(message);
        }

        if (!root.TryGetProperty("idToken", out var token))
        {
            throw new InvalidOperationException(
                "Firebase did not return an authentication token.");
        }

        return token.GetString()
            ?? throw new InvalidOperationException(
                "Firebase returned an empty authentication token.");
    }
}
