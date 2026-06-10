using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using IoclFleetApi.Data;
using IoclFleetApi.DTOs;
using IoclFleetApi.Hubs;
using IoclFleetApi.Models;
using IoclFleetApi.Services;

namespace IoclFleetApi.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "SUPER_ADMIN")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IComplianceService _compliance;
    private readonly IAuditService _audit;

    public AdminController(AppDbContext db, IComplianceService compliance, IAuditService audit)
    {
        _db = db;
        _compliance = compliance;
        _audit = audit;
    }

    [HttpPost("trigger-compliance-emails")]
    public async Task<IActionResult> TriggerComplianceEmails(
        [FromServices] IEmailService emailService,
        [FromServices] IHubContext<ComplianceHub> hubContext,
        [FromServices] IComplianceAlertDispatcher dispatcher)
    {
        var userIdStr = User.FindFirst("id")?.Value;
        int? userId = string.IsNullOrEmpty(userIdStr) ? null : int.Parse(userIdStr);
        var username = User.FindFirst("username")?.Value ?? "unknown";
        
        Console.WriteLine($"[AdminAPI] Manual compliance email scan triggered by: {username}");

        var records = await _db.ComplianceRecords
            .Include(r => r.Vehicle!)
            .ThenInclude(v => v.Department)
            .ToListAsync();

        int alertCount = 0;
        int emailsSent = 0;

        foreach (var record in records)
        {
            if (string.IsNullOrEmpty(record.ExpiryDate) || record.ExpiryDate == "PENDING") continue;

            var currentStatus = record.Status;
            var computedStatus = _compliance.CalculateStatus(record.ExpiryDate);

            if (currentStatus != computedStatus)
            {
                record.Status = computedStatus;
                record.LastUpdatedTimestamp = DateTime.UtcNow;

                var vehicle = record.Vehicle;
                if (vehicle == null) continue;

                await _compliance.UpdateVehicleStatus(vehicle.Id);

                if (computedStatus != "ACTIVE")
                {
                    alertCount++;
                    var today = DateTime.Today;
                    var expiry = DateTime.Parse(record.ExpiryDate).Date;
                    var diffDays = (int)Math.Ceiling((expiry - today).TotalDays);

                    var alertMessage = $"{record.LicenseType} certificate for vehicle {vehicle.VehicleNumber} is now {computedStatus} ({diffDays} days remaining).";

                    var notification = new Notification
                    {
                        VehicleId = vehicle.Id,
                        DepartmentId = vehicle.DepartmentId,
                        Title = $"Compliance Alert: {record.LicenseType}",
                        Message = alertMessage,
                        Type = computedStatus == "EXPIRED" ? "EXPIRED" : computedStatus == "WARNING" ? "WARNING" : "CRITICAL",
                        Status = "UNREAD"
                    };
                    _db.Notifications.Add(notification);

                    var socketPayload = new
                    {
                        id = notification.Id,
                        vehicleId = vehicle.Id,
                        vehicleNumber = vehicle.VehicleNumber,
                        departmentId = vehicle.DepartmentId,
                        title = notification.Title,
                        message = notification.Message,
                        type = notification.Type,
                        createdAt = notification.CreatedAt
                    };

                    await hubContext.Clients.Group($"dept-{vehicle.DepartmentId}")
                        .SendAsync("compliance_alert", socketPayload);
                    await hubContext.Clients.Group("super-admins")
                        .SendAsync("compliance_alert", socketPayload);

                    dispatcher.DispatchComplianceAlert(notification);

                    var usersToNotify = await _db.Users
                        .Where(u => u.Status == "ACTIVE" && (u.Role == "SUPER_ADMIN" || (u.Role == "DEPT_ADMIN" && u.DepartmentId == vehicle.DepartmentId)))
                        .ToListAsync();

                    var uniqueRecipients = usersToNotify
                        .GroupBy(u => u.Email.ToLower().Trim())
                        .Select(g => g.First())
                        .ToList();

                    foreach (var user in uniqueRecipients)
                    {
                        try
                        {
                            await emailService.SendComplianceAlert(
                                user.Email,
                                user.Username,
                                vehicle.VehicleNumber,
                                vehicle.VehicleType,
                                vehicle.Department?.Name ?? "N/A",
                                record.LicenseType,
                                record.ExpiryDate,
                                diffDays,
                                computedStatus
                            );
                            emailsSent++;
                        }
                        catch (Exception emailEx)
                        {
                            Console.WriteLine($"[AdminAPI] Failed to send email alert to {user.Email}: {emailEx.Message}");
                        }
                    }
                }
            }
        }

        await _db.SaveChangesAsync();

        await _audit.LogAction(userId, username, "TRIGGER_EMAIL_SCAN",
            $"Manually triggered compliance scan. Generated {alertCount} new alerts and sent {emailsSent} alert emails.",
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(ApiResponse.Ok(new { alertsGenerated = alertCount, emailsSent }, "Compliance email scan run successfully."));
    }

    [HttpPost("trigger-daily-digest")]
    public async Task<IActionResult> TriggerDailyDigest([FromServices] IEmailService emailService)
    {
        var userIdStr = User.FindFirst("id")?.Value;
        int? userId = string.IsNullOrEmpty(userIdStr) ? null : int.Parse(userIdStr);
        var username = User.FindFirst("username")?.Value ?? "unknown";
        
        Console.WriteLine($"[AdminAPI] Manual daily digest triggered by: {username}");

        int emailsSent = await SendDailyDigestEmailsInternal(emailService);

        await _audit.LogAction(userId, username, "TRIGGER_DAILY_DIGEST",
            $"Manually triggered daily digest emails. Dispatched summary reports to {emailsSent} users.",
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(ApiResponse.Ok(new { emailsSent }, $"Daily summary digest dispatched to {emailsSent} users."));
    }

    private async Task<int> SendDailyDigestEmailsInternal(IEmailService emailService)
    {
        var vehicles = await _db.Vehicles
            .Include(v => v.ComplianceRecords)
            .ToListAsync();

        int totalVehicles = vehicles.Count;
        int expiredCount = 0;
        int criticalCount = 0;
        int warningCount = 0;

        foreach (var v in vehicles)
        {
            foreach (var r in v.ComplianceRecords)
            {
                var status = _compliance.CalculateStatus(r.ExpiryDate);
                if (status == "EXPIRED") expiredCount++;
                else if (status is "HIGH_CRITICAL" or "MEDIUM_CRITICAL") criticalCount++;
                else if (status == "WARNING") warningCount++;
            }
        }

        var departments = await _db.Departments
            .Include(d => d.Vehicles)
            .ThenInclude(v => v.ComplianceRecords)
            .ToListAsync();

        var breakdown = departments.Select(d =>
        {
            int totalLicenses = 0;
            int compliantLicenses = 0;

            foreach (var v in d.Vehicles)
            {
                foreach (var r in v.ComplianceRecords)
                {
                    totalLicenses++;
                    var status = _compliance.CalculateStatus(r.ExpiryDate);
                    if (status == "ACTIVE" || status == "WARNING")
                        compliantLicenses++;
                }
            }

            var score = totalLicenses > 0 ? Math.Round((double)compliantLicenses / totalLicenses * 100, 1) : 100.0;

            return new DepartmentBreakdown
            {
                Name = d.Name,
                VehicleCount = d.Vehicles.Count,
                ComplianceScore = score
            };
        }).ToList();

        var expiringRecords = await _db.ComplianceRecords
            .Include(c => c.Vehicle!).ThenInclude(v => v.Department)
            .Where(c => c.Status == "EXPIRED" || c.Status == "HIGH_CRITICAL"
                     || c.Status == "MEDIUM_CRITICAL" || c.Status == "WARNING")
            .ToListAsync();

        var usersToNotify = await _db.Users
            .Where(u => u.Status == "ACTIVE" && (u.Role == "SUPER_ADMIN" || u.Role == "DEPT_ADMIN"))
            .ToListAsync();

        var uniqueRecipients = usersToNotify
            .GroupBy(u => u.Email.ToLower().Trim())
            .Select(g => g.First())
            .ToList();

        int sentCount = 0;
        foreach (var user in uniqueRecipients)
        {
            try
            {
                await emailService.SendDailySummary(
                    user.Email,
                    user.Username,
                    totalVehicles,
                    expiredCount,
                    criticalCount,
                    warningCount,
                    breakdown,
                    expiringRecords
                );
                sentCount++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AdminAPI] Failed to send digest email to {user.Email}: {ex.Message}");
            }
        }

        return sentCount;
    }
}
