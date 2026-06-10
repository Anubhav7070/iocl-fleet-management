using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IoclFleetApi.Data;
using IoclFleetApi.DTOs;

namespace IoclFleetApi.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public NotificationsController(AppDbContext db) => _db = db;

    private (string role, int? departmentId) GetCurrentUser()
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)!.Value;
        var id = int.Parse(User.FindFirst("id")!.Value);
        var user = _db.Users.Find(id);
        return (role, user?.DepartmentId);
    }

    [HttpGet]
    public async Task<IActionResult> GetNotifications()
    {
        var (role, deptId) = GetCurrentUser();
        var query = _db.Notifications
            .Include(n => n.Vehicle)
            .AsQueryable();

        if (role == "DEPT_ADMIN")
            query = query.Where(n => n.DepartmentId == deptId || n.DepartmentId == null);

        var notifications = await query.OrderByDescending(n => n.CreatedAt).Take(100).ToListAsync();
        return Ok(ApiResponse.Ok(notifications, "Notifications loaded."));
    }

    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        var (role, deptId) = GetCurrentUser();
        var notification = await _db.Notifications.FindAsync(id);
        if (notification == null) return NotFound(ApiResponse.Fail("Notification not found."));

        if (role == "DEPT_ADMIN" && notification.DepartmentId != null && notification.DepartmentId != deptId)
            return StatusCode(403, ApiResponse.Fail("Access denied."));

        notification.Status = "READ";
        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(notification, "Notification marked as read."));
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var (role, deptId) = GetCurrentUser();
        var query = _db.Notifications.Where(n => n.Status == "UNREAD");
        if (role == "DEPT_ADMIN") query = query.Where(n => n.DepartmentId == deptId);

        var notifications = await query.ToListAsync();
        foreach (var n in notifications) n.Status = "READ";
        await _db.SaveChangesAsync();

        return Ok(ApiResponse.Ok(null, "All notifications marked as read."));
    }
}
