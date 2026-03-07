using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Authorization;

namespace PlaNEvent.Client.Services;

public sealed class AuthService(HttpClient httpClient, TokenAuthenticationStateProvider authStateProvider)
{
    public async Task<bool> LoginAsync(string email, string password)
    {
        var response = await httpClient.PostAsJsonAsync("api/auth/login", new { Email = email, Password = password });
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var payload = await response.Content.ReadFromJsonAsync<AuthResponse>();
        if (payload is null)
        {
            return false;
        }

        await authStateProvider.SetTokenAsync(payload.Token);
        return true;
    }

    public async Task<bool> RegisterAsync(string email, string password, string displayName, bool registerAsCustomer)
    {
        var response = await httpClient.PostAsJsonAsync("api/auth/register", new
        {
            Email = email,
            Password = password,
            DisplayName = displayName,
            RegisterAsCustomer = registerAsCustomer
        });

        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var payload = await response.Content.ReadFromJsonAsync<AuthResponse>();
        if (payload is null)
        {
            return false;
        }

        await authStateProvider.SetTokenAsync(payload.Token);
        return true;
    }

    public async Task LogoutAsync() => await authStateProvider.SetTokenAsync(string.Empty);

    private sealed record AuthResponse(string Token, DateTime ExpiresAtUtc, object Profile);
}
