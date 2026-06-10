using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using IoclFleetApi.Models;
using IoclFleetApi.Data;
using Microsoft.EntityFrameworkCore;

namespace IoclFleetApi.Services;

public class BlazorAuthStateProvider : AuthenticationStateProvider
{
    private readonly ProtectedSessionStorage _sessionStorage;
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ClaimsPrincipal _anonymous = new ClaimsPrincipal(new ClaimsIdentity());

    public BlazorAuthStateProvider(ProtectedSessionStorage sessionStorage, AppDbContext db, IAuditService audit)
    {
        _sessionStorage = sessionStorage;
        _db = db;
        _audit = audit;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var userSessionResult = await _sessionStorage.GetAsync<UserSession>("UserSession");
            var userSession = userSessionResult.Success ? userSessionResult.Value : null;
            if (userSession == null)
                return new AuthenticationState(_anonymous);

            var claimsPrincipal = CreateClaimsPrincipal(userSession);
            return new AuthenticationState(claimsPrincipal);
        }
        catch
        {
            return new AuthenticationState(_anonymous);
        }
    }

    public async Task<string?> LoginAsync(string username, string password, string? ipAddress)
    {
        var user = await _db.Users
            .Include(u => u.Department)
            .FirstOrDefaultAsync(u => u.Username == username);

        if (user == null || user.Status != "ACTIVE" || !BCrypt.Net.BCrypt.Verify(password, user.Password))
        {
            return "Invalid username or password.";
        }

        var userSession = new UserSession
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            Role = user.Role,
            DepartmentId = user.DepartmentId,
            DepartmentName = user.Department?.Name
        };

        await _sessionStorage.SetAsync("UserSession", userSession);

        var claimsPrincipal = CreateClaimsPrincipal(userSession);
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(claimsPrincipal)));

        await _audit.LogAction(user.Id, user.Username, "USER_LOGIN",
            $"User {user.Username} logged in via Web Interface.",
            ipAddress: ipAddress,
            departmentId: user.DepartmentId);

        return null; // Success
    }

    public async Task LogoutAsync()
    {
        await _sessionStorage.DeleteAsync("UserSession");
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_anonymous)));
    }

    private ClaimsPrincipal CreateClaimsPrincipal(UserSession session)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>
        {
            new Claim(ClaimTypes.Name, session.Username),
            new Claim("username", session.Username),
            new Claim(ClaimTypes.Role, session.Role),
            new Claim("id", session.Id.ToString()),
            new Claim(ClaimTypes.Email, session.Email),
            new Claim("DepartmentId", session.DepartmentId?.ToString() ?? ""),
            new Claim("DepartmentName", session.DepartmentName ?? "")
        }, "CustomAuth"));
    }
}

public class UserSession
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
}
