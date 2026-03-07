using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace PlaNEvent.Client.Services;

public sealed class TokenAuthenticationStateProvider(IJSRuntime jsRuntime) : AuthenticationStateProvider
{
    private const string TokenKey = "planevent.auth.token";
    private string? _cachedToken;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        _cachedToken ??= await jsRuntime.InvokeAsync<string>("planeventAuth.getToken", TokenKey);

        if (string.IsNullOrWhiteSpace(_cachedToken))
        {
            return Anonymous();
        }

        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(_cachedToken))
        {
            return Anonymous();
        }

        var jwt = handler.ReadJwtToken(_cachedToken);
        if (jwt.ValidTo < DateTime.UtcNow)
        {
            await SetTokenAsync(string.Empty);
            return Anonymous();
        }

        var identity = new ClaimsIdentity(jwt.Claims, "jwt");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    public async Task SetTokenAsync(string token)
    {
        _cachedToken = token;
        await jsRuntime.InvokeVoidAsync("planeventAuth.setToken", TokenKey, token);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public async Task<string?> GetTokenAsync()
    {
        _cachedToken ??= await jsRuntime.InvokeAsync<string>("planeventAuth.getToken", TokenKey);
        return _cachedToken;
    }

    private static AuthenticationState Anonymous() => new(new ClaimsPrincipal(new ClaimsIdentity()));
}
