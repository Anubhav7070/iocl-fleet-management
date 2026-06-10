namespace IoclFleetApi.DTOs;

/// <summary>
/// Standard API response wrapper matching the Node.js frontend contract
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
    public string Timestamp { get; set; } = DateTime.UtcNow.ToString("o");
    public object? Errors { get; set; }

    public static ApiResponse<T> Ok(T? data, string message = "Success")
        => new() { Success = true, Data = data, Message = message };

    public static ApiResponse<T> Fail(string message, object? errors = null)
        => new() { Success = false, Message = message, Errors = errors };
}

public class ApiResponse : ApiResponse<object>
{
    public new static ApiResponse Ok(object? data = null, string message = "Success")
        => new() { Success = true, Data = data, Message = message };

    public new static ApiResponse Fail(string message, object? errors = null)
        => new() { Success = false, Message = message, Errors = errors };
}

// Auth
public class LoginDto
{
    public string? Username { get; set; }
    public string? Password { get; set; }
}

// Vehicle
public class CreateVehicleDto
{
    public string? VehicleNumber { get; set; }
    public string? VehicleType { get; set; }
    public string? DriverName { get; set; }
    public string? VendorName { get; set; }
    public int? DepartmentId { get; set; }
}

public class UpdateVehicleDto
{
    public string? VehicleType { get; set; }
    public string? DriverName { get; set; }
    public string? VendorName { get; set; }
    public int? DepartmentId { get; set; }
}

// User
public class CreateUserDto
{
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? Role { get; set; }
    public int? DepartmentId { get; set; }
}

public class UpdateUserDto
{
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? Role { get; set; }
    public int? DepartmentId { get; set; }
    public string? Status { get; set; }
}

// Department
public class CreateDepartmentDto
{
    public string? Name { get; set; }
    public string? Code { get; set; }
    public string? Description { get; set; }
}

public class UpdateDepartmentDto
{
    public string? Name { get; set; }
    public string? Code { get; set; }
    public string? Description { get; set; }
}

// Compliance
public class RenewComplianceDto
{
    public string? LicenseNumber { get; set; }
    public string? IssuingAuthority { get; set; }
    public string? IssueDate { get; set; }
    public string? ExpiryDate { get; set; }
}

public class VerifyDocumentDto
{
    public bool IsVerified { get; set; }
}

// Dashboard
public class DashboardStatsDto
{
    public DashboardCounts Counts { get; set; } = new();
    public List<UpcomingExpiryDto> UpcomingExpiries { get; set; } = new();
    public List<object> RecentNotifications { get; set; } = new();
    public List<object> RecentAudits { get; set; } = new();
    public List<DepartmentComparisonDto> DepartmentComparison { get; set; } = new();
    public List<object> StatusDistribution { get; set; } = new();
}

public class DashboardCounts
{
    public int TotalVehicles { get; set; }
    public int FullyCompliant { get; set; }
    public int Warning { get; set; }
    public int Critical { get; set; }
    public int Expired { get; set; }
}

public class UpcomingExpiryDto
{
    public int Id { get; set; }
    public string VehicleNumber { get; set; } = string.Empty;
    public string VehicleType { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string LicenseType { get; set; } = string.Empty;
    public string? LicenseNumber { get; set; }
    public string? ExpiryDate { get; set; }
    public int DaysRemaining { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class DepartmentComparisonDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public double ComplianceScore { get; set; }
    public int VehicleCount { get; set; }
    public int CompliantCount { get; set; }
}

public class GateEntryLogDto
{
    public int VehicleId { get; set; }
    public bool Allowed { get; set; }
    public string? Remarks { get; set; }
}
