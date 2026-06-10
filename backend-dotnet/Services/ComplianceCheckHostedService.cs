using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using IoclFleetApi.Data;
using IoclFleetApi.Hubs;
using IoclFleetApi.Models;

namespace IoclFleetApi.Services;

/// <summary>
/// Background service replacing node-cron.
/// Runs compliance check on startup (5s delay) and every 12 hours.
/// </summary>
public class ComplianceCheckHostedService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ComplianceCheckHostedService> _logger;
    private DateTime? _lastDigestSentDate;

    public ComplianceCheckHostedService(IServiceProvider services, ILogger<ComplianceCheckHostedService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait 5 seconds after startup
        await Task.Delay(5000, stoppingToken);
        _logger.LogInformation("[ComplianceCheck] Initial compliance scan starting...");
        await RunComplianceCheck(stoppingToken);

        // Then run every 12 hours
        using var timer = new PeriodicTimer(TimeSpan.FromHours(12));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            _logger.LogInformation("[ComplianceCheck] Periodic compliance scan starting...");
            await RunComplianceCheck(stoppingToken);
        }
    }

    private async Task RunComplianceCheck(CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var complianceService = scope.ServiceProvider.GetRequiredService<IComplianceService>();
            var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<ComplianceHub>>();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IComplianceAlertDispatcher>();

            var today = DateTime.Today;

            // ── 1. Update compliance statuses in the database first ────
            var records = await db.ComplianceRecords
                .Include(r => r.Vehicle!)
                .ThenInclude(v => v.Department)
                .ToListAsync(ct);

            int alertCount = 0;

            foreach (var record in records)
            {
                if (string.IsNullOrEmpty(record.ExpiryDate) || record.ExpiryDate == "PENDING") continue;

                var currentStatus = record.Status;
                var computedStatus = complianceService.CalculateStatus(record.ExpiryDate);

                if (currentStatus != computedStatus)
                {
                    record.Status = computedStatus;
                    record.LastUpdatedTimestamp = DateTime.UtcNow;

                    var vehicle = record.Vehicle;
                    if (vehicle == null) continue;

                    await complianceService.UpdateVehicleStatus(vehicle.Id);

                    if (computedStatus != "ACTIVE")
                    {
                        alertCount++;
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
                        db.Notifications.Add(notification);
                        await db.SaveChangesAsync(ct);

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
                            .SendAsync("compliance_alert", socketPayload, ct);
                        await hubContext.Clients.Group("super-admins")
                            .SendAsync("compliance_alert", socketPayload, ct);

                        dispatcher.DispatchComplianceAlert(notification);

                        // ── Send compliance alert email to matching admins ──
                        try
                        {
                            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                            var usersToNotify = await db.Users
                                .Where(u => u.Status == "ACTIVE" && (u.Role == "SUPER_ADMIN" || (u.Role == "DEPT_ADMIN" && u.DepartmentId == vehicle.DepartmentId)))
                                .ToListAsync(ct);

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
                                }
                                catch (Exception emailEx)
                                {
                                    _logger.LogError(emailEx, "[ComplianceCheck] Failed to send email alert to {Email}", user.Email);
                                }
                            }
                        }
                        catch (Exception alertEmailEx)
                        {
                            _logger.LogError(alertEmailEx, "[ComplianceCheck] Failed to look up users or send compliance alert emails");
                        }
                    }
                }
            }

            await db.SaveChangesAsync(ct);
            _logger.LogInformation("[ComplianceCheck] Scan complete. Generated {AlertCount} new alerts.", alertCount);

            // ── 2. Send Daily digest summaries once per calendar day (using updated database statuses) ────
            if (_lastDigestSentDate == null || today > _lastDigestSentDate.Value.Date)
            {
                _logger.LogInformation("[ComplianceCheck] Starting scheduled daily compliance digest email send...");
                try
                {
                    var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                    await SendDailyDigestEmailsHosted(db, complianceService, emailService, ct);
                    _lastDigestSentDate = today;
                }
                catch (Exception digestEx)
                {
                    _logger.LogError(digestEx, "[ComplianceCheck] Failed to send scheduled daily digest emails");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ComplianceCheck] Error during compliance scan");
        }
    }

    private async Task SendDailyDigestEmailsHosted(AppDbContext db, IComplianceService complianceService, IEmailService emailService, CancellationToken ct)
    {
        var vehicles = await db.Vehicles
            .Include(v => v.ComplianceRecords)
            .ToListAsync(ct);

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
            .ToListAsync(ct);

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
            .ToListAsync(ct);

        var usersToNotify = await db.Users
            .Where(u => u.Status == "ACTIVE" && (u.Role == "SUPER_ADMIN" || u.Role == "DEPT_ADMIN"))
            .ToListAsync(ct);

        var uniqueRecipients = usersToNotify
            .GroupBy(u => u.Email.ToLower().Trim())
            .Select(g => g.First())
            .ToList();

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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ComplianceCheck] Failed to send scheduled digest email to {Email}", user.Email);
            }
        }
    }
}
