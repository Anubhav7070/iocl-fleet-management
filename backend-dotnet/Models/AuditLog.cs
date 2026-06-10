using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoclFleetApi.Models;

[Table("audit_logs")]
public class AuditLog
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("userId")]
    public int? UserId { get; set; }

    [Column("username")]
    public string? Username { get; set; }

    [Required]
    [Column("action")]
    public string Action { get; set; } = string.Empty;

    [Column("description")]
    public string? Description { get; set; }

    [Column("oldValue")]
    public string? OldValue { get; set; }

    [Column("newValue")]
    public string? NewValue { get; set; }

    [Column("departmentId")]
    public int? DepartmentId { get; set; }

    [Column("ipAddress")]
    public string? IpAddress { get; set; }

    [Column("vehicleId")]
    public int? VehicleId { get; set; }

    [Column("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("UserId")]
    public User? User { get; set; }

    [ForeignKey("DepartmentId")]
    public Department? DepartmentObject { get; set; }

    [ForeignKey("VehicleId")]
    public Vehicle? Vehicle { get; set; }
}
