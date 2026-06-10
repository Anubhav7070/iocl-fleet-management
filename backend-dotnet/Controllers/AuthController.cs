using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IoclFleetApi.Data;
using IoclFleetApi.DTOs;
using IoclFleetApi.Services;

namespace IoclFleetApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IJwtService _jwt;
    private readonly IAuditService _audit;

    public AuthController(AppDbContext db, IJwtService jwt, IAuditService audit)
    {
        _db = db;
        _jwt = jwt;
        _audit = audit;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        if (string.IsNullOrEmpty(dto.Username) || string.IsNullOrEmpty(dto.Password))
            return BadRequest(ApiResponse.Fail("Username and password are required."));

        var user = await _db.Users
            .Include(u => u.Department)
            .FirstOrDefaultAsync(u => u.Username == dto.Username);

        if (user == null)
            return Unauthorized(ApiResponse.Fail("Invalid username or password."));

        if (user.Status != "ACTIVE")
            return StatusCode(403, ApiResponse.Fail("Your user account is deactivated."));

        if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.Password))
            return Unauthorized(ApiResponse.Fail("Invalid username or password."));

        var token = _jwt.GenerateToken(user);

        await _audit.LogAction(user.Id, user.Username, "USER_LOGIN",
            $"User {user.Username} logged in successfully.",
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            departmentId: user.DepartmentId);

        return Ok(ApiResponse.Ok(new
        {
            token,
            user = new
            {
                user.Id,
                user.Username,
                user.Email,
                user.Role,
                user.DepartmentId,
                department = user.Department != null ? new
                {
                    user.Department.Id,
                    user.Department.Name,
                    user.Department.Code,
                    user.Department.Division
                } : null
            }
        }, "Login successful."));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        var userId = int.Parse(User.FindFirst("id")!.Value);
        var user = await _db.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return Unauthorized(ApiResponse.Fail("User session invalid."));

        return Ok(ApiResponse.Ok(new
        {
            user = new
            {
                user.Id,
                user.Username,
                user.Email,
                user.Role,
                user.DepartmentId,
                department = user.Department != null ? new
                {
                    user.Department.Id,
                    user.Department.Name,
                    user.Department.Code,
                    user.Department.Division
                } : null
            }
        }, "Current user context retrieved."));
    }
}
