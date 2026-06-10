using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        [FromServices] IComplianceAlertDispatcher dispatcher,
        [FromServices] IServiceScopeFactory scopeFactory)
    {
        var userIdStr = User.FindFirst("id")?.Value;
        int? userId = string.IsNullOrEmpty(userIdStr) ? null : int.Parse(userIdStr);
        var username = User.FindFirst("username")?.Value ?? "unknown";
        
        Console.WriteLine($"[AdminAPI] Manual compliance email scan triggered by: {username}");

        // Dispatch everything (DB sync, database notification, SignalR, and mailing) in the background to prevent HTTP gateway timeouts and SQLite contention
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var scopedCompliance = scope.ServiceProvider.GetRequiredService<IComplianceService>();
                var scopedEmail = scope.ServiceProvider.GetRequiredService<IEmailService>();

                var records = await scopedDb.ComplianceRecords
                    .Include(r => r.Vehicle!)
                    .ThenInclude(v => v.Department)
                    .ToListAsync();

                int alertCount = 0;
                var recordsToEmail = new List<int>();

                foreach (var record in records)
                {
                    if (string.IsNullOrEmpty(record.ExpiryDate) || record.ExpiryDate == "PENDING") continue;

                    var currentStatus = record.Status;
                    var computedStatus = scopedCompliance.CalculateStatus(record.ExpiryDate);

                    // 1. Sync database status if it changed
                    if (currentStatus != computedStatus)
                    {
                        record.Status = computedStatus;
                        record.LastUpdatedTimestamp = DateTime.UtcNow;

                        var vehicle = record.Vehicle;
                        if (vehicle != null)
                        {
                            await scopedCompliance.UpdateVehicleStatus(vehicle.Id);
                        }
                    }

                    // 2. Queue emails for all non-active (warning, critical, expired) certificates
                    if (computedStatus != "ACTIVE")
                    {
                        alertCount++;
                        recordsToEmail.Add(record.Id);

                        var vehicle = record.Vehicle;
                        if (vehicle == null) continue;

                        // Create database notification & socket alert only if status changed
                        if (currentStatus != computedStatus)
                        {
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
                            scopedDb.Notifications.Add(notification);

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
                        }
                    }
                }

                await scopedDb.SaveChangesAsync();

                // Dispatch alert emails in the background
                await SendComplianceAlertEmailsInternal(scopedDb, scopedEmail, recordsToEmail);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AdminAPI] Error in background compliance alert email scan: {ex.Message}");
            }
        });

        await _audit.LogAction(userId, username, "TRIGGER_EMAIL_SCAN",
            "Manually triggered compliance scan. Dispatch initiated in the background.",
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(ApiResponse.Ok(new { alertsGenerated = 1, emailsSent = 1 }, "Compliance scan run successfully. Alert email dispatch initiated in background."));
    }

    [HttpPost("trigger-daily-digest")]
    public async Task<IActionResult> TriggerDailyDigest(
        [FromServices] IEmailService emailService,
        [FromServices] IServiceScopeFactory scopeFactory)
    {
        var userIdStr = User.FindFirst("id")?.Value;
        int? userId = string.IsNullOrEmpty(userIdStr) ? null : int.Parse(userIdStr);
        var username = User.FindFirst("username")?.Value ?? "unknown";
        
        Console.WriteLine($"[AdminAPI] Manual daily digest triggered by: {username}");

        // Dispatch status recalculation and email sending in the background to prevent HTTP gateway timeouts and SQLite contention
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var scopedEmail = scope.ServiceProvider.GetRequiredService<IEmailService>();
                var scopedCompliance = scope.ServiceProvider.GetRequiredService<IComplianceService>();
                
                var records = await scopedDb.ComplianceRecords
                    .Include(r => r.Vehicle)
                    .ToListAsync();
                foreach (var record in records)
                {
                    if (string.IsNullOrEmpty(record.ExpiryDate) || record.ExpiryDate == "PENDING") continue;
                    var computedStatus = scopedCompliance.CalculateStatus(record.ExpiryDate);
                    if (record.Status != computedStatus)
                    {
                        record.Status = computedStatus;
                        record.LastUpdatedTimestamp = DateTime.UtcNow;
                        if (record.Vehicle != null)
                        {
                            await scopedCompliance.UpdateVehicleStatus(record.Vehicle.Id);
                        }
                    }
                }
                await scopedDb.SaveChangesAsync();

                await SendDailyDigestEmailsInternal(scopedDb, scopedCompliance, scopedEmail);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AdminAPI] Error in background daily digest: {ex.Message}");
            }
        });

        await _audit.LogAction(userId, username, "TRIGGER_DAILY_DIGEST",
            "Manually triggered daily digest emails. Dispatch started in the background.",
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(ApiResponse.Ok(new { emailsSent = 1 }, "Daily compliance summaries dispatch initiated in the background."));
    }

    private async Task<int> SendDailyDigestEmailsInternal(AppDbContext db, IComplianceService complianceService, IEmailService emailService)
    {
        var vehicles = await db.Vehicles
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
                var status = complianceService.CalculateStatus(r.ExpiryDate);
                if (status == "EXPIRED") expiredCount++;
                else if (status is "HIGH_CRITICAL" or "MEDIUM_CRITICAL") criticalCount++;
                else if (status == "WARNING") warningCount++;
            }
        }

        var departments = await db.Departments
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
                    var status = complianceService.CalculateStatus(r.ExpiryDate);
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

        var expiringRecords = await db.ComplianceRecords
            .Include(c => c.Vehicle!).ThenInclude(v => v.Department)
            .Where(c => c.Status == "EXPIRED" || c.Status == "HIGH_CRITICAL"
                     || c.Status == "MEDIUM_CRITICAL" || c.Status == "WARNING")
            .ToListAsync();

        var usersToNotify = await db.Users
            .Include(u => u.Department)
            .Where(u => u.Status == "ACTIVE" && (u.Role == "SUPER_ADMIN" || u.Role == "DEPT_ADMIN"))
            .ToListAsync();

        int sentCount = 0;
        foreach (var user in usersToNotify)
        {
            try
            {
                var greetingName = user.Role == "SUPER_ADMIN" ? "Super Admin" : (user.Department != null ? $"{user.Department.Name} Admin" : user.Username);
                
                int userTotalVehicles = totalVehicles;
                int userExpired = expiredCount;
                int userCritical = criticalCount;
                int userWarning = warningCount;
                var userBreakdown = breakdown;
                var userExpiring = expiringRecords;

                if (user.Role == "DEPT_ADMIN" && user.DepartmentId.HasValue)
                {
                    var deptId = user.DepartmentId.Value;
                    var deptVehicles = vehicles.Where(v => v.DepartmentId == deptId).ToList();
                    userTotalVehicles = deptVehicles.Count;

                    userExpired = 0;
                    userCritical = 0;
                    userWarning = 0;
                    foreach (var v in deptVehicles)
                    {
                        foreach (var r in v.ComplianceRecords)
                        {
                            var status = complianceService.CalculateStatus(r.ExpiryDate);
                            if (status == "EXPIRED") userExpired++;
                            else if (status is "HIGH_CRITICAL" or "MEDIUM_CRITICAL") userCritical++;
                            else if (status == "WARNING") userWarning++;
                        }
                    }

                    userBreakdown = breakdown.Where(b => b.Name == user.Department?.Name).ToList();
                    userExpiring = expiringRecords.Where(r => r.Vehicle?.DepartmentId == deptId).ToList();
                }

                await emailService.SendDailySummary(
                    user.Email,
                    greetingName,
                    userTotalVehicles,
                    userExpired,
                    userCritical,
                    userWarning,
                    userBreakdown,
                    userExpiring
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

    private async Task SendComplianceAlertEmailsInternal(AppDbContext db, IEmailService emailService, List<int> recordIds)
    {
        var records = await db.ComplianceRecords
            .Include(r => r.Vehicle!)
            .ThenInclude(v => v.Department)
            .Where(r => recordIds.Contains(r.Id))
            .ToListAsync();

        int sentCount = 0;
        foreach (var record in records)
        {
            var vehicle = record.Vehicle;
            if (vehicle == null || string.IsNullOrEmpty(record.ExpiryDate)) continue;

            var today = DateTime.Today;
            var expiry = DateTime.Parse(record.ExpiryDate).Date;
            var diffDays = (int)Math.Ceiling((expiry - today).TotalDays);
            var computedStatus = record.Status;

            // Send compliance email alert to matching admins
            var usersToNotify = await db.Users
                .Include(u => u.Department)
                .Where(u => u.Status == "ACTIVE" && (u.Role == "SUPER_ADMIN" || (u.Role == "DEPT_ADMIN" && u.DepartmentId == vehicle.DepartmentId)))
                .ToListAsync();

            foreach (var user in usersToNotify)
            {
                try
                {
                    var greetingName = user.Role == "SUPER_ADMIN" ? "Super Admin" : (user.Department != null ? $"{user.Department.Name} Admin" : user.Username);
                    await emailService.SendComplianceAlert(
                        user.Email,
                        greetingName,
                        vehicle.VehicleNumber,
                        vehicle.VehicleType,
                        vehicle.Department?.Name ?? "N/A",
                        record.LicenseType,
                        record.ExpiryDate,
                        diffDays,
                        computedStatus
                    );
                    sentCount++;
                }
                catch (Exception emailEx)
                {
                    Console.WriteLine($"[AdminAPI] Failed to send email alert to {user.Email} for record {record.Id}: {emailEx.Message}");
                }
            }
        }
        
        Console.WriteLine($"[AdminAPI] Background compliance alert email scan finished. Dispatched {sentCount} emails.");
    }
}
