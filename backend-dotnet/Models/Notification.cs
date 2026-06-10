using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoclFleetApi.Models;

[Table("notifications")]
public class Notification
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("vehicleId")]
    public int? VehicleId { get; set; }

    [Column("departmentId")]
    public int? DepartmentId { get; set; }

    [Required]
    [Column("title")]
    public string Title { get; set; } = string.Empty;

    [Required]
    [Column("message")]
    public string Message { get; set; } = string.Empty;

    [Required]
    [Column("status")]
    public string Status { get; set; } = "UNREAD"; // UNREAD, READ

    [Required]
    [Column("type")]
    public string Type { get; set; } = string.Empty; // WARNING, CRITICAL, EXPIRED, RENEWAL

    [Column("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("VehicleId")]
    public Vehicle? Vehicle { get; set; }

    [ForeignKey("DepartmentId")]
    public Department? Department { get; set; }
}
