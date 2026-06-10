using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.SignalR.Client;
using System.Security.Claims;
using IoclFleetApi.DTOs;

namespace IoclFleetApi.Services;

public class NotificationClientService : IAsyncDisposable
{
    private readonly ProtectedSessionStorage _sessionStorage;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly string _backendUrl;
    private HubConnection? _hubConnection;

    public event Action<NotificationDto>? OnNotificationReceived;
    public event Action<object>? OnComplianceRenewedReceived;

    public NotificationClientService(ProtectedSessionStorage sessionStorage, AuthenticationStateProvider authStateProvider, IConfiguration config)
    {
        _sessionStorage = sessionStorage;
        _authStateProvider = authStateProvider;
        _backendUrl = config["Backend:Url"] ?? "http://localhost:5000";
    }

    public async Task StartAsync()
    {
        if (_hubConnection != null) return;

        var tokenResult = await _sessionStorage.GetAsync<string>("iocl_session_token");
        var token = tokenResult.Success ? tokenResult.Value : null;

        var hubUrl = $"{_backendUrl}/hubs/compliance";
        if (!string.IsNullOrEmpty(token))
        {
            hubUrl += $"?access_token={Uri.EscapeDataString(token)}";
        }

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<NotificationDto>("compliance_alert", (notification) =>
        {
            OnNotificationReceived?.Invoke(notification);
        });

        _hubConnection.On<object>("compliance_renewed", (payload) =>
        {
            OnComplianceRenewedReceived?.Invoke(payload);
        });

        try
        {
            await _hubConnection.StartAsync();

            var authState = await _authStateProvider.GetAuthenticationStateAsync();
            if (authState.User.Identity?.IsAuthenticated == true)
            {
                var role = authState.User.FindFirst(ClaimTypes.Role)?.Value;
                var deptIdStr = authState.User.FindFirst("DepartmentId")?.Value;
                int? departmentId = string.IsNullOrEmpty(deptIdStr) ? null : int.Parse(deptIdStr);

                await _hubConnection.SendAsync("JoinSession", role, departmentId);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SignalR Client] Connection failed: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync();
        }
    }
}
