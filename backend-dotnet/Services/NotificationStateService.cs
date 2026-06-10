using System.Security.Claims;
using IoclFleetApi.Models;
using Microsoft.AspNetCore.Components.Authorization;

namespace IoclFleetApi.Services;

public class NotificationStateService : IDisposable
{
    private readonly IComplianceAlertDispatcher _dispatcher;
    private readonly AuthenticationStateProvider _authStateProvider;
    
    public event Action<Notification>? OnNotificationReceived;
    public event Action<object>? OnComplianceRenewedReceived;

    private ClaimsPrincipal? _user;
    private bool _initialized;

    public NotificationStateService(IComplianceAlertDispatcher dispatcher, AuthenticationStateProvider authStateProvider)
    {
        _dispatcher = dispatcher;
        _authStateProvider = authStateProvider;
        _dispatcher.OnComplianceAlert += HandleComplianceAlert;
        _dispatcher.OnComplianceRenewed += HandleComplianceRenewed;
    }

    private async Task EnsureUserLoaded()
    {
        if (_initialized) return;
        try
        {
            var authState = await _authStateProvider.GetAuthenticationStateAsync();
            _user = authState.User;
            _initialized = true;
        }
        catch
        {
            // Fail silently if auth state cannot be read during initialization
        }
    }

    private async void HandleComplianceAlert(Notification notification)
    {
        await EnsureUserLoaded();
        if (_user?.Identity?.IsAuthenticated != true) return;

        var role = _user.FindFirst(ClaimTypes.Role)?.Value;
        var deptIdStr = _user.FindFirst("DepartmentId")?.Value;
        int? userDeptId = string.IsNullOrEmpty(deptIdStr) ? null : int.Parse(deptIdStr);

        // Check if user has permission to see this notification
        bool shouldNotify = false;
        if (role == "SUPER_ADMIN")
        {
            shouldNotify = true;
        }
        else if (role == "DEPT_ADMIN" && notification.DepartmentId == userDeptId)
        {
            shouldNotify = true;
        }

        if (shouldNotify)
        {
            OnNotificationReceived?.Invoke(notification);
        }
    }

    private async void HandleComplianceRenewed(object payload)
    {
        await EnsureUserLoaded();
        if (_user?.Identity?.IsAuthenticated != true) return;

        OnComplianceRenewedReceived?.Invoke(payload);
    }

    public void Dispose()
    {
        _dispatcher.OnComplianceAlert -= HandleComplianceAlert;
        _dispatcher.OnComplianceRenewed -= HandleComplianceRenewed;
    }
}
