using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoclFleetApi.Models;

[Table("renewal_history")]
public class RenewalHistory
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("complianceRecordId")]
    public int ComplianceRecordId { get; set; }

    [Required]
    [Column("vehicleId")]
    public int VehicleId { get; set; }

    [Required]
    [Column("licenseType")]
    public string LicenseType { get; set; } = string.Empty;

    [Column("oldExpiryDate")]
    public string? OldExpiryDate { get; set; }

    [Column("newExpiryDate")]
    public string? NewExpiryDate { get; set; }

    [Column("oldDocumentId")]
    public int? OldDocumentId { get; set; }

    [Column("newDocumentId")]
    public int? NewDocumentId { get; set; }

    [Column("renewedBy")]
    public int? RenewedBy { get; set; }

    [Column("renewedAt")]
    public DateTime RenewedAt { get; set; } = DateTime.UtcNow;

    [Column("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("ComplianceRecordId")]
    public ComplianceRecord? ComplianceRecord { get; set; }

    [ForeignKey("VehicleId")]
    public Vehicle? Vehicle { get; set; }

    [ForeignKey("OldDocumentId")]
    public Document? OldDocument { get; set; }

    [ForeignKey("NewDocumentId")]
    public Document? NewDocument { get; set; }

    [ForeignKey("RenewedBy")]
    public User? RenewedByUser { get; set; }
}
