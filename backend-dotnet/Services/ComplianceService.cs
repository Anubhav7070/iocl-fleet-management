using Microsoft.EntityFrameworkCore;
using IoclFleetApi.Data;
using IoclFleetApi.Models;

namespace IoclFleetApi.Services;

public interface IComplianceService
{
    string CalculateStatus(string? expiryDate);
    Task<string> UpdateVehicleStatus(int vehicleId);
    Task UpdateDepartmentScore(int departmentId);
}

public class ComplianceService : IComplianceService
{
    private readonly AppDbContext _db;

    public ComplianceService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Calculates license status based on expiry date.
    /// Matches the Node.js logic exactly: >30d=ACTIVE, ≤30d=WARNING, ≤15d=MEDIUM_CRITICAL, ≤7d=HIGH_CRITICAL, ≤0=EXPIRED
    /// </summary>
    public string CalculateStatus(string? expiryDate)
    {
        if (string.IsNullOrEmpty(expiryDate)) return "ACTIVE";

        if (!DateTime.TryParse(expiryDate, out var expiry)) return "ACTIVE";

        var today = DateTime.Today;
        expiry = expiry.Date;
        var diffDays = (int)Math.Ceiling((expiry - today).TotalDays);

        if (diffDays <= 0) return "EXPIRED";
        if (diffDays <= 7) return "HIGH_CRITICAL";
        if (diffDays <= 15) return "MEDIUM_CRITICAL";
        if (diffDays <= 30) return "WARNING";
        return "ACTIVE";
    }

    /// <summary>
    /// Recalculates and updates the overall status of a vehicle based on its compliance records.
    /// </summary>
    public async Task<string> UpdateVehicleStatus(int vehicleId)
    {
        var vehicle = await _db.Vehicles
            .Include(v => v.ComplianceRecords)
            .FirstOrDefaultAsync(v => v.Id == vehicleId);

        if (vehicle == null) throw new Exception($"Vehicle with ID {vehicleId} not found");

        var records = vehicle.ComplianceRecords;
        if (records == null || records.Count == 0)
        {
            vehicle.OverallStatus = "FULLY_COMPLIANT";
            await _db.SaveChangesAsync();
            return vehicle.OverallStatus;
        }

        bool hasExpired = false, hasCritical = false, hasWarning = false;

        foreach (var record in records)
        {
            var computedStatus = CalculateStatus(record.ExpiryDate);
            if (record.Status != computedStatus)
            {
                record.Status = computedStatus;
            }

            switch (computedStatus)
            {
                case "EXPIRED": hasExpired = true; break;
                case "HIGH_CRITICAL":
                case "MEDIUM_CRITICAL": hasCritical = true; break;
                case "WARNING": hasWarning = true; break;
            }
        }

        vehicle.OverallStatus = hasExpired ? "EXPIRED"
            : hasCritical ? "CRITICAL"
            : hasWarning ? "WARNING"
            : "FULLY_COMPLIANT";

        await _db.SaveChangesAsync();
        await UpdateDepartmentScore(vehicle.DepartmentId);
        return vehicle.OverallStatus;
    }

    /// <summary>
    /// Updates a department's compliance score based on its vehicles' compliance records.
    /// </summary>
    public async Task UpdateDepartmentScore(int departmentId)
    {
        var department = await _db.Departments
            .Include(d => d.Vehicles)
            .ThenInclude(v => v.ComplianceRecords)
            .FirstOrDefaultAsync(d => d.Id == departmentId);

        if (department == null) return;

        int totalLicenses = 0;
        int compliantLicenses = 0;

        foreach (var vehicle in department.Vehicles)
        {
            foreach (var record in vehicle.ComplianceRecords)
            {
                totalLicenses++;
                var status = CalculateStatus(record.ExpiryDate);
                if (status == "ACTIVE" || status == "WARNING")
                    compliantLicenses++;
            }
        }

        var score = totalLicenses > 0 ? Math.Round((double)compliantLicenses / totalLicenses * 100, 1) : 100.0;
        department.ComplianceScore = score;
        await _db.SaveChangesAsync();
    }
}
