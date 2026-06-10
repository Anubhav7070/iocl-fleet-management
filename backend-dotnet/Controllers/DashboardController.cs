using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IoclFleetApi.Data;
using IoclFleetApi.DTOs;
using IoclFleetApi.Services;

namespace IoclFleetApi.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IComplianceService _compliance;

    public DashboardController(AppDbContext db, IComplianceService compliance)
    {
        _db = db;
        _compliance = compliance;
    }

    private (int id, string role, int? departmentId) GetCurrentUser()
    {
        var id = int.Parse(User.FindFirst("id")!.Value);
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)!.Value;
        var user = _db.Users.Find(id);
        return (id, role, user?.DepartmentId);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetDashboardStats()
    {
        var (_, role, deptId) = GetCurrentUser();
        var isDeptAdmin = role == "DEPT_ADMIN";

        var stats = new DashboardStatsDto();

        // 1. Vehicle counts
        var vehicleQuery = _db.Vehicles.AsQueryable();
        if (isDeptAdmin) vehicleQuery = vehicleQuery.Where(v => v.DepartmentId == deptId);

        var vehicles = await vehicleQuery.Select(v => new { v.Id, v.OverallStatus, v.DepartmentId }).ToListAsync();
        stats.Counts.TotalVehicles = vehicles.Count;
        stats.Counts.FullyCompliant = vehicles.Count(v => v.OverallStatus == "FULLY_COMPLIANT");
        stats.Counts.Warning = vehicles.Count(v => v.OverallStatus == "WARNING");
        stats.Counts.Critical = vehicles.Count(v => v.OverallStatus == "CRITICAL");
        stats.Counts.Expired = vehicles.Count(v => v.OverallStatus == "EXPIRED");

        // 2. Upcoming expiries (next 30 days)
        var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
        var thirtyDays = DateTime.Today.AddDays(30).ToString("yyyy-MM-dd");

        var expiryQuery = _db.ComplianceRecords
            .Include(c => c.Vehicle!).ThenInclude(v => v.Department)
            .Where(c => c.ExpiryDate != null && string.Compare(c.ExpiryDate, todayStr) >= 0 && string.Compare(c.ExpiryDate, thirtyDays) <= 0)
            .AsQueryable();

        if (isDeptAdmin) expiryQuery = expiryQuery.Where(c => c.Vehicle!.DepartmentId == deptId);

        var upcomingExpiries = await expiryQuery.OrderBy(c => c.ExpiryDate).Take(10).ToListAsync();
        stats.UpcomingExpiries = upcomingExpiries.Select(r =>
        {
            var days = (int)Math.Ceiling((DateTime.Parse(r.ExpiryDate!) - DateTime.Today).TotalDays);
            return new UpcomingExpiryDto
            {
                Id = r.Id,
                VehicleNumber = r.Vehicle?.VehicleNumber ?? "",
                VehicleType = r.Vehicle?.VehicleType ?? "",
                DepartmentName = r.Vehicle?.Department?.Name ?? "Unknown",
                LicenseType = r.LicenseType,
                LicenseNumber = r.LicenseNumber,
                ExpiryDate = r.ExpiryDate,
                DaysRemaining = days,
                Status = r.Status
            };
        }).ToList();

        // 3. Recent notifications
        var notifQuery = _db.Notifications
            .Include(n => n.Vehicle)
            .AsQueryable();
        if (isDeptAdmin)
            notifQuery = notifQuery.Where(n => n.DepartmentId == deptId || n.DepartmentId == null);

        stats.RecentNotifications = (await notifQuery.OrderByDescending(n => n.CreatedAt).Take(8).ToListAsync())
            .Select(n => (object)new
            {
                n.Id, n.VehicleId, n.DepartmentId, n.Title, n.Message, n.Status, n.Type, n.CreatedAt,
                vehicle = n.Vehicle != null ? new { n.Vehicle.Id, n.Vehicle.VehicleNumber } : null
            }).ToList();

        // 4. Recent audits
        var auditQuery = _db.AuditLogs.AsQueryable();
        if (isDeptAdmin) auditQuery = auditQuery.Where(a => a.DepartmentId == deptId);

        stats.RecentAudits = (await auditQuery.OrderByDescending(a => a.CreatedAt).Take(8).ToListAsync())
            .Select(a => (object)a).ToList();

        // 5. Department comparisons
        var deptQuery = _db.Departments
            .Include(d => d.Vehicles).ThenInclude(v => v.ComplianceRecords)
            .AsQueryable();
        if (isDeptAdmin) deptQuery = deptQuery.Where(d => d.Id == deptId);

        var departments = await deptQuery.OrderByDescending(d => d.ComplianceScore).ToListAsync();
        stats.DepartmentComparison = departments.Select(d =>
        {
            var vehicleCount = d.Vehicles.Count;
            int totalLicenses = 0, compliantLicenses = 0, compliantCount = 0;
            foreach (var v in d.Vehicles)
            {
                if (v.OverallStatus == "FULLY_COMPLIANT" || v.OverallStatus == "WARNING") compliantCount++;
                foreach (var rec in v.ComplianceRecords)
                {
                    totalLicenses++;
                    var status = _compliance.CalculateStatus(rec.ExpiryDate);
                    if (status == "ACTIVE" || status == "WARNING") compliantLicenses++;
                }
            }
            var freshScore = totalLicenses > 0 ? Math.Round((double)compliantLicenses / totalLicenses * 100, 1) : 100.0;
            if (d.ComplianceScore != freshScore)
            {
                d.ComplianceScore = freshScore;
            }
            return new DepartmentComparisonDto
            {
                Id = d.Id, Name = d.Name, Code = d.Code,
                ComplianceScore = freshScore, VehicleCount = vehicleCount, CompliantCount = compliantCount
            };
        }).ToList();

        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(stats, "Dashboard aggregates generated."));
    }

    [HttpGet("uploaded-documents")]
    public async Task<IActionResult> GetUploadedDocuments()
    {
        var (_, role, _) = GetCurrentUser();
        if (role != "SUPER_ADMIN")
            return StatusCode(403, ApiResponse.Fail("Access denied. Only Super Admin can view all documents."));

        var vehiclesWithDocs = await _db.Vehicles
            .Where(v => v.DocumentId != null)
            .Include(v => v.Department)
            .Include(v => v.RegistrationDocument)
            .ToListAsync();

        var complianceWithDocs = await _db.ComplianceRecords
            .Where(c => c.DocumentId != null)
            .Include(c => c.Vehicle!).ThenInclude(v => v.Department)
            .Include(c => c.Document)
            .ToListAsync();

        var docs = new List<object>();

        foreach (var v in vehiclesWithDocs)
        {
            if (v.RegistrationDocument == null) continue;
            docs.Add(new
            {
                id = $"vehicle-{v.Id}",
                type = "VEHICLE_RC",
                recordId = v.Id,
                vehicleNumber = v.VehicleNumber,
                departmentName = v.Department?.Name ?? "N/A",
                documentType = "Registration Certificate (RC)",
                fileName = v.RegistrationDocument.FileName,
                filePath = v.RegistrationDocument.FilePath,
                uploadedAt = v.RegistrationDocument.CreatedAt,
                isVerified = v.IsVerified,
                verifiedBy = v.VerifiedBy
            });
        }

        foreach (var c in complianceWithDocs)
        {
            if (c.Document == null) continue;
            docs.Add(new
            {
                id = $"compliance-{c.Id}",
                type = "COMPLIANCE",
                recordId = c.Id,
                vehicleNumber = c.Vehicle?.VehicleNumber ?? "N/A",
                departmentName = c.Vehicle?.Department?.Name ?? "N/A",
                documentType = c.LicenseType.Replace("_", " "),
                fileName = c.Document.FileName,
                filePath = c.Document.FilePath,
                uploadedAt = c.Document.CreatedAt,
                isVerified = c.IsVerified,
                verifiedBy = c.VerifiedBy
            });
        }

        docs = docs.OrderByDescending(d => ((dynamic)d).uploadedAt).ToList();
        return Ok(ApiResponse.Ok(docs, "All uploaded documents fetched successfully."));
    }
}
