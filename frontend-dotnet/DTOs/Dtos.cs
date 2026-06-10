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
}

public class ApiResponse : ApiResponse<object>
{
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
    public List<NotificationDto> RecentNotifications { get; set; } = new();
    public List<AuditLogDto> RecentAudits { get; set; } = new();
    public List<DepartmentComparisonDto> DepartmentComparison { get; set; } = new();
    public List<StatusDistributionDto> StatusDistribution { get; set; } = new();
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

// Helper models for deserializing API responses
public class DepartmentDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Division { get; set; } = string.Empty;
    public double ComplianceScore { get; set; }
    public int TotalVehicles { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int? DepartmentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DepartmentDto? Department { get; set; }
    public DateTime CreatedAt { get; set; }
    public string DepartmentName => Department?.Name ?? string.Empty;
}

public class UserContainerDto
{
    public UserDto User { get; set; } = new();
}

public class DocumentDto
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public int FileSize { get; set; }
}

public class VehicleDto
{
    public int Id { get; set; }
    public string VehicleNumber { get; set; } = string.Empty;
    public string VehicleType { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string VendorName { get; set; } = string.Empty;
    public int DepartmentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string OverallStatus { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DepartmentDto? Department { get; set; }
    public List<ComplianceRecordDto> ComplianceRecords { get; set; } = new();
    public bool IsVerified { get; set; }
    public string? VerifiedBy { get; set; }
    public int? DocumentId { get; set; }
    public string? QrCodeUrl { get; set; }
    public DocumentDto? RegistrationDocument { get; set; }
}

public class ComplianceRecordDto
{
    public int Id { get; set; }
    public int VehicleId { get; set; }
    public string LicenseType { get; set; } = string.Empty;
    public string LicenseNumber { get; set; } = string.Empty;
    public string IssuingAuthority { get; set; } = string.Empty;
    public string IssueDate { get; set; } = string.Empty;
    public string ExpiryDate { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? DocumentId { get; set; }
    public bool IsVerified { get; set; }
    public string? VerifiedBy { get; set; }
    public string? LastUpdatedBy { get; set; }
    public DateTime? LastUpdatedTimestamp { get; set; }
    public VehicleDto? Vehicle { get; set; }
    public DocumentDto? Document { get; set; }
}

public class RenewalHistoryDto
{
    public int Id { get; set; }
    public int ComplianceRecordId { get; set; }
    public int VehicleId { get; set; }
    public string LicenseType { get; set; } = string.Empty;
    public string OldExpiryDate { get; set; } = string.Empty;
    public string NewExpiryDate { get; set; } = string.Empty;
    public string RenewedBy { get; set; } = string.Empty;
    public DateTime RenewedAt { get; set; }
    public int? OldDocumentId { get; set; }
    public int? NewDocumentId { get; set; }
    public VehicleDto? Vehicle { get; set; }
    public DocumentDto? OldDocument { get; set; }
    public DocumentDto? NewDocument { get; set; }
}

public class NotificationDto
{
    public int Id { get; set; }
    public int? VehicleId { get; set; }
    public int? DepartmentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = "UNREAD";
    public string Type { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class AuditLogDto
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Details { get => Description; set => Description = value; }
    public string? IpAddress { get; set; }
    public int? DepartmentId { get; set; }
    public int? VehicleId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime Timestamp { get => CreatedAt; set => CreatedAt = value; }
}

public class StatusDistributionDto
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class LoginResponseDto
{
    public string Token { get; set; } = string.Empty;
    public UserDto User { get; set; } = new();
}

public class UploadedDocumentDto
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // VEHICLE_RC, COMPLIANCE
    public int RecordId { get; set; }
    public string VehicleNumber { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public bool IsVerified { get; set; }
    public string? VerifiedBy { get; set; }
}

public class GateEntryLogDto
{
    public int VehicleId { get; set; }
    public bool Allowed { get; set; }
    public string? Remarks { get; set; }
}

