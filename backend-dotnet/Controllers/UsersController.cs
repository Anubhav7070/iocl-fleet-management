using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IoclFleetApi.Data;
using IoclFleetApi.DTOs;
using IoclFleetApi.Services;

namespace IoclFleetApi.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = "SUPER_ADMIN")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public UsersController(AppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    private (int id, string username) GetCurrentUser()
    {
        var id = int.Parse(User.FindFirst("id")!.Value);
        var username = User.FindFirst("username")!.Value;
        return (id, username);
    }

    [HttpGet]
    public async Task<IActionResult> GetAllUsers()
    {
        var users = await _db.Users
            .Include(u => u.Department)
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new
            {
                u.Id, u.Username, u.Email, u.Role, u.Status, u.CreatedAt,
                department = u.Department != null ? new { u.Department.Id, u.Department.Name, u.Department.Code } : null
            })
            .ToListAsync();

        return Ok(ApiResponse.Ok(users, "Users list loaded."));
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
    {
        if (string.IsNullOrEmpty(dto.Username) || string.IsNullOrEmpty(dto.Email) ||
            string.IsNullOrEmpty(dto.Password) || string.IsNullOrEmpty(dto.Role))
            return BadRequest(ApiResponse.Fail("Username, email, password, and role are required."));

        var existing = await _db.Users.FirstOrDefaultAsync(u => u.Username == dto.Username);
        if (existing != null)
            return BadRequest(ApiResponse.Fail("Username is already taken."));

        var (userId, adminUsername) = GetCurrentUser();
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

        var user = new Models.User
        {
            Username = dto.Username.ToLower().Trim(),
            Email = dto.Email.ToLower().Trim(),
            Password = passwordHash,
            Role = dto.Role,
            DepartmentId = (dto.Role == "SUPER_ADMIN" || dto.Role == "GATEMAN") ? null : dto.DepartmentId,
            Status = "ACTIVE"
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        await _audit.LogAction(userId, adminUsername, "CREATE_USER",
            $"Created user: {dto.Username} with role {dto.Role}.",
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        return StatusCode(201, ApiResponse.Ok(new
        {
            user.Id, user.Username, user.Email, user.Role, user.DepartmentId, user.Status, user.CreatedAt
        }, "User registered successfully."));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserDto dto)
    {
        var (adminId, adminUsername) = GetCurrentUser();
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound(ApiResponse.Fail("User not found."));

        var oldValue = new { user.Email, user.Role, user.Status, user.DepartmentId };

        if (!string.IsNullOrEmpty(dto.Email)) user.Email = dto.Email;
        if (!string.IsNullOrEmpty(dto.Role)) user.Role = dto.Role;
        if (!string.IsNullOrEmpty(dto.Status)) user.Status = dto.Status;
        user.DepartmentId = (user.Role == "SUPER_ADMIN" || user.Role == "GATEMAN") ? null : (dto.DepartmentId ?? user.DepartmentId);

        if (!string.IsNullOrEmpty(dto.Password))
            user.Password = BCrypt.Net.BCrypt.HashPassword(dto.Password);

        await _db.SaveChangesAsync();

        await _audit.LogAction(adminId, adminUsername, "UPDATE_USER",
            $"Updated user profile for {user.Username}.",
            oldValue: oldValue,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(ApiResponse.Ok(new
        {
            user.Id, user.Username, user.Email, user.Role, user.DepartmentId, user.Status, user.CreatedAt
        }, "User updated successfully."));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var (adminId, adminUsername) = GetCurrentUser();
        if (id == adminId)
            return BadRequest(ApiResponse.Fail("You cannot delete your own account."));

        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound(ApiResponse.Fail("User not found."));

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        await _audit.LogAction(adminId, adminUsername, "DELETE_USER",
            $"Deleted user profile: {user.Username}.",
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(ApiResponse.Ok(null, "User deleted successfully."));
    }
}
