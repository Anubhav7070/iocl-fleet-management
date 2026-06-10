using System.Text.Json;
using IoclFleetApi.Data;
using IoclFleetApi.Models;

namespace IoclFleetApi.Services;

public interface IAuditService
{
    Task LogAction(int? userId, string? username, string action, string? description,
        object? oldValue = null, object? newValue = null,
        int? departmentId = null, string? ipAddress = null, int? vehicleId = null);
}

public class AuditService : IAuditService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AuditService> _logger;

    public AuditService(AppDbContext db, ILogger<AuditService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task LogAction(int? userId, string? username, string action, string? description,
        object? oldValue = null, object? newValue = null,
        int? departmentId = null, string? ipAddress = null, int? vehicleId = null)
    {
        try
        {
            var log = new AuditLog
            {
                UserId = userId,
                Username = username,
                Action = action,
                Description = description,
                OldValue = oldValue != null ? JsonSerializer.Serialize(oldValue) : null,
                NewValue = newValue != null ? JsonSerializer.Serialize(newValue) : null,
                DepartmentId = departmentId,
                IpAddress = ipAddress,
                VehicleId = vehicleId
            };
            _db.AuditLogs.Add(log);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AuditService] Failed to create audit log");
        }
    }
}
