using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace IoclFleetApi.Models;

[Table("users")]
public class User
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("username")]
    public string Username { get; set; } = string.Empty;

    [Required]
    [Column("email")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [JsonIgnore]
    [Column("password")]
    public string Password { get; set; } = string.Empty;

    [Required]
    [Column("role")]
    public string Role { get; set; } = string.Empty; // SUPER_ADMIN, DEPT_ADMIN, VIEWER

    [Column("departmentId")]
    public int? DepartmentId { get; set; }

    [Required]
    [Column("status")]
    public string Status { get; set; } = "ACTIVE"; // ACTIVE, INACTIVE

    [Column("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("DepartmentId")]
    public Department? Department { get; set; }

    public ICollection<Document> Documents { get; set; } = new List<Document>();
    public ICollection<RenewalHistory> Renewals { get; set; } = new List<RenewalHistory>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
    public ICollection<Report> Reports { get; set; } = new List<Report>();
}
