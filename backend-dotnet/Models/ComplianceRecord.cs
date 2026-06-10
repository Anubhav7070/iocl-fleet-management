using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoclFleetApi.Models;

[Table("compliance_records")]
public class ComplianceRecord
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("vehicleId")]
    public int VehicleId { get; set; }

    [Required]
    [Column("licenseType")]
    public string LicenseType { get; set; } = string.Empty;
    // ROAD_PERMIT, AGE_DETERMINATION, PUC, FITNESS, EXPLOSIVE, GREEN_CARD, INSURANCE, CALIBRATION

    [Column("licenseNumber")]
    public string? LicenseNumber { get; set; }

    [Column("issuingAuthority")]
    public string? IssuingAuthority { get; set; }

    [Column("issueDate")]
    public string? IssueDate { get; set; } // DATEONLY stored as string YYYY-MM-DD

    [Column("expiryDate")]
    public string? ExpiryDate { get; set; } // DATEONLY stored as string YYYY-MM-DD

    [Required]
    [Column("status")]
    public string Status { get; set; } = "ACTIVE";
    // ACTIVE, WARNING, MEDIUM_CRITICAL, HIGH_CRITICAL, EXPIRED

    [Column("documentId")]
    public int? DocumentId { get; set; }

    [Column("lastUpdatedBy")]
    public string? LastUpdatedBy { get; set; }

    [Column("lastUpdatedTimestamp")]
    public DateTime LastUpdatedTimestamp { get; set; } = DateTime.UtcNow;

    [Column("isVerified")]
    public bool IsVerified { get; set; } = false;

    [Column("verifiedBy")]
    public string? VerifiedBy { get; set; }

    [Column("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("VehicleId")]
    public Vehicle? Vehicle { get; set; }

    [ForeignKey("DocumentId")]
    public Document? Document { get; set; }

    public ICollection<RenewalHistory> Renewals { get; set; } = new List<RenewalHistory>();
}
