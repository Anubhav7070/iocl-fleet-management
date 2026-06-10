using Microsoft.AspNetCore.SignalR;

namespace IoclFleetApi.Hubs;

/// <summary>
/// SignalR hub replacing Socket.io for real-time compliance alerts.
/// Event names match the original: compliance_alert, compliance_renewed
/// </summary>
public class ComplianceHub : Hub
{
    private readonly ILogger<ComplianceHub> _logger;

    public ComplianceHub(ILogger<ComplianceHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("[SignalR] Client connected: {Id}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("[SignalR] Client disconnected: {Id}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Client calls this after connecting to join appropriate groups.
    /// Mirrors socket.emit('join_session', { role, departmentId })
    /// </summary>
    public async Task JoinSession(string? role, int? departmentId)
    {
        if (role == "SUPER_ADMIN")
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "super-admins");
        }

        if (departmentId.HasValue)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"dept-{departmentId.Value}");
        }
    }
}
