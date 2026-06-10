using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoclFleetApi.Models;

[Table("vehicles")]
public class Vehicle
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("vehicleNumber")]
    public string VehicleNumber { get; set; } = string.Empty;

    [Required]
    [Column("vehicleType")]
    public string VehicleType { get; set; } = string.Empty;

    [Required]
    [Column("departmentId")]
    public int DepartmentId { get; set; }

    [Column("driverName")]
    public string? DriverName { get; set; }

    [Column("vendorName")]
    public string? VendorName { get; set; }

    [Column("qrCodeUrl")]
    public string? QrCodeUrl { get; set; }

    [Required]
    [Column("overallStatus")]
    public string OverallStatus { get; set; } = "FULLY_COMPLIANT";

    [Column("documentId")]
    public int? DocumentId { get; set; }

    [Column("lastUpdatedBy")]
    public string? LastUpdatedBy { get; set; }

    [Column("lastUpdatedTimestamp")]
    public DateTime? LastUpdatedTimestamp { get; set; }

    [Column("isVerified")]
    public bool IsVerified { get; set; } = false;

    [Column("verifiedBy")]
    public string? VerifiedBy { get; set; }

    [Column("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("DepartmentId")]
    public Department? Department { get; set; }

    [ForeignKey("DocumentId")]
    public Document? RegistrationDocument { get; set; }

    public ICollection<ComplianceRecord> ComplianceRecords { get; set; } = new List<ComplianceRecord>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public ICollection<RenewalHistory> Renewals { get; set; } = new List<RenewalHistory>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
