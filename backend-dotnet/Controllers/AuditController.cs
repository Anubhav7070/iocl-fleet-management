using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IoclFleetApi.Data;
using IoclFleetApi.DTOs;

namespace IoclFleetApi.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize(Roles = "SUPER_ADMIN,DEPT_ADMIN")]
public class AuditController : ControllerBase
{
    private readonly AppDbContext _db;

    public AuditController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAuditLogs([FromQuery] string? search, [FromQuery] int? departmentId)
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)!.Value;
        var userId = int.Parse(User.FindFirst("id")!.Value);
        var user = await _db.Users.FindAsync(userId);

        var query = _db.AuditLogs.AsQueryable();

        if (role == "DEPT_ADMIN")
            query = query.Where(a => a.DepartmentId == user!.DepartmentId);
        else if (departmentId.HasValue)
            query = query.Where(a => a.DepartmentId == departmentId.Value);

        if (!string.IsNullOrEmpty(search))
            query = query.Where(a =>
                (a.Username != null && a.Username.Contains(search)) ||
                a.Action.Contains(search) ||
                (a.Description != null && a.Description.Contains(search)));

        var logs = await query.OrderByDescending(a => a.CreatedAt).Take(150).ToListAsync();
        return Ok(ApiResponse.Ok(logs, "Audit logs fetched successfully."));
    }
}
