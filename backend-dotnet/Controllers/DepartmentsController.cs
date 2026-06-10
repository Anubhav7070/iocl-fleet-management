using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IoclFleetApi.Data;
using IoclFleetApi.DTOs;
using IoclFleetApi.Services;

namespace IoclFleetApi.Controllers;

[ApiController]
[Route("api/departments")]
[Authorize]
public class DepartmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly IComplianceService _compliance;

    public DepartmentsController(AppDbContext db, IAuditService audit, IComplianceService compliance)
    {
        _db = db;
        _audit = audit;
        _compliance = compliance;
    }

    private (int id, string username) GetCurrentUser()
    {
        var id = int.Parse(User.FindFirst("id")!.Value);
        var username = User.FindFirst("username")!.Value;
        return (id, username);
    }

    [HttpGet]
    public async Task<IActionResult> GetAllDepartments()
    {
        var departments = await _db.Departments
            .Include(d => d.Vehicles).ThenInclude(v => v.ComplianceRecords)
            .OrderBy(d => d.Name)
            .ToListAsync();

        foreach (var dept in departments)
        {
            int totalLicenses = 0, compliantLicenses = 0;
            foreach (var vehicle in dept.Vehicles)
            {
                foreach (var record in vehicle.ComplianceRecords)
                {
                    totalLicenses++;
                    var status = _compliance.CalculateStatus(record.ExpiryDate);
                    if (status == "ACTIVE" || status == "WARNING") compliantLicenses++;
                }
            }
            var freshScore = totalLicenses > 0 ? Math.Round((double)compliantLicenses / totalLicenses * 100, 1) : 100.0;
            if (dept.ComplianceScore != freshScore) dept.ComplianceScore = freshScore;
        }
        await _db.SaveChangesAsync();

        var result = departments.Select(d => new
        {
            d.Id, d.Name, d.Code, d.Description, d.Division, d.ComplianceScore, d.CreatedAt, d.UpdatedAt
        });

        return Ok(ApiResponse.Ok(result, "Departments list retrieved."));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDepartmentById(int id)
    {
        var department = await _db.Departments
            .Include(d => d.Users)
            .Include(d => d.Vehicles).ThenInclude(v => v.ComplianceRecords)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (department == null)
            return NotFound(ApiResponse.Fail("Department not found."));

        int totalLicenses = 0, compliantLicenses = 0;
        foreach (var vehicle in department.Vehicles)
        {
            foreach (var record in vehicle.ComplianceRecords)
            {
                totalLicenses++;
                var status = _compliance.CalculateStatus(record.ExpiryDate);
                if (status == "ACTIVE" || status == "WARNING") compliantLicenses++;
            }
        }
        var freshScore = totalLicenses > 0 ? Math.Round((double)compliantLicenses / totalLicenses * 100, 1) : 100.0;
        if (department.ComplianceScore != freshScore)
        {
            department.ComplianceScore = freshScore;
            await _db.SaveChangesAsync();
        }

        return Ok(ApiResponse.Ok(new
        {
            department.Id, department.Name, department.Code, department.Description,
            department.Division, department.ComplianceScore, department.CreatedAt, department.UpdatedAt,
            users = department.Users.Select(u => new { u.Id, u.Username, u.Email, u.Role, u.Status }),
            vehicleCount = department.Vehicles.Count
        }, "Department details loaded."));
    }

    [Authorize(Roles = "SUPER_ADMIN")]
    [HttpPost]
    public async Task<IActionResult> CreateDepartment([FromBody] CreateDepartmentDto dto)
    {
        if (string.IsNullOrEmpty(dto.Name) || string.IsNullOrEmpty(dto.Code))
            return BadRequest(ApiResponse.Fail("Name and code are required fields."));

        var (userId, username) = GetCurrentUser();
        var dept = new Models.Department { Name = dto.Name, Code = dto.Code, Description = dto.Description };
        _db.Departments.Add(dept);
        await _db.SaveChangesAsync();

        await _audit.LogAction(userId, username, "CREATE_DEPARTMENT",
            $"Created department: {dto.Name} ({dto.Code})",
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        return StatusCode(201, ApiResponse.Ok(new
        {
            dept.Id, dept.Name, dept.Code, dept.Description, dept.Division, dept.ComplianceScore, dept.CreatedAt, dept.UpdatedAt
        }, "Department created successfully."));
    }

    [Authorize(Roles = "SUPER_ADMIN")]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateDepartment(int id, [FromBody] UpdateDepartmentDto dto)
    {
        var (userId, username) = GetCurrentUser();
        var dept = await _db.Departments.FindAsync(id);
        if (dept == null) return NotFound(ApiResponse.Fail("Department not found."));

        var oldValue = new { dept.Name, dept.Code, dept.Description };
        if (!string.IsNullOrEmpty(dto.Name)) dept.Name = dto.Name;
        if (!string.IsNullOrEmpty(dto.Code)) dept.Code = dto.Code;
        if (dto.Description != null) dept.Description = dto.Description;
        await _db.SaveChangesAsync();

        await _audit.LogAction(userId, username, "UPDATE_DEPARTMENT",
            $"Updated department: {dept.Name} ({dept.Code})",
            oldValue: oldValue,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(ApiResponse.Ok(new
        {
            dept.Id, dept.Name, dept.Code, dept.Description, dept.Division, dept.ComplianceScore, dept.CreatedAt, dept.UpdatedAt
        }, "Department updated successfully."));
    }

    [Authorize(Roles = "SUPER_ADMIN")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteDepartment(int id)
    {
        var (userId, username) = GetCurrentUser();
        var dept = await _db.Departments.FindAsync(id);
        if (dept == null) return NotFound(ApiResponse.Fail("Department not found."));

        var vehicleCount = await _db.Vehicles.CountAsync(v => v.DepartmentId == id);
        var userCount = await _db.Users.CountAsync(u => u.DepartmentId == id);
        if (vehicleCount > 0 || userCount > 0)
            return BadRequest(ApiResponse.Fail($"Cannot delete department. It contains {vehicleCount} vehicles and {userCount} users."));

        _db.Departments.Remove(dept);
        await _db.SaveChangesAsync();

        await _audit.LogAction(userId, username, "DELETE_DEPARTMENT",
            $"Deleted department: {dept.Name} ({dept.Code})",
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(ApiResponse.Ok(null, "Department deleted successfully."));
    }
}
