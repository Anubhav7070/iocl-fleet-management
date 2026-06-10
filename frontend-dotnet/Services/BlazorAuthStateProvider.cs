using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using IoclFleetApi.DTOs;

namespace IoclFleetApi.Services;

public class BlazorAuthStateProvider : AuthenticationStateProvider
{
    private readonly ApiService _api;
    private readonly ProtectedSessionStorage _sessionStorage;
    private readonly ClaimsPrincipal _anonymous = new ClaimsPrincipal(new ClaimsIdentity());
    private UserDto? _cachedUser;

    public BlazorAuthStateProvider(ApiService api, ProtectedSessionStorage sessionStorage)
    {
        _api = api;
        _sessionStorage = sessionStorage;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            if (_cachedUser != null)
            {
                return new AuthenticationState(CreateClaimsPrincipal(_cachedUser));
            }

            var tokenResult = await _sessionStorage.GetAsync<string>("iocl_session_token");
            var token = tokenResult.Success ? tokenResult.Value : null;

            if (string.IsNullOrEmpty(token))
            {
                return new AuthenticationState(_anonymous);
            }

            var user = await _api.GetMeAsync();
            _cachedUser = user;
            return new AuthenticationState(CreateClaimsPrincipal(user));
        }
        catch (InvalidOperationException)
        {
            // Fallback during server-side prerendering when JS interop is not yet available
            return new AuthenticationState(_anonymous);
        }
        catch
        {
            try
            {
                await _sessionStorage.DeleteAsync("iocl_session_token");
            }
            catch
            {
                // Ignore errors if JS interop is not active
            }
            _cachedUser = null;
            return new AuthenticationState(_anonymous);
        }
    }

    public async Task LoginAsync(string username, string password)
    {
        var response = await _api.LoginAsync(username, password);
        _cachedUser = response.User;
        
        var claimsPrincipal = CreateClaimsPrincipal(response.User);
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(claimsPrincipal)));
    }

    public async Task LogoutAsync()
    {
        await _sessionStorage.DeleteAsync("iocl_session_token");
        _cachedUser = null;
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_anonymous)));
    }

    private ClaimsPrincipal CreateClaimsPrincipal(UserDto user)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
        {
            new Claim(ClaimTypes.Name, user.Username),
            new Claim("username", user.Username),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("id", user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("DepartmentId", user.DepartmentId?.ToString() ?? ""),
            new Claim("DepartmentName", user.Department?.Name ?? "")
        }, "CustomAuth"));
    }
}
