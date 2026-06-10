using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoclFleetApi.Models;

[Table("reports")]
public class Report
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("title")]
    public string Title { get; set; } = string.Empty;

    [Required]
    [Column("type")]
    public string Type { get; set; } = string.Empty; // PDF, EXCEL

    [Column("departmentId")]
    public int? DepartmentId { get; set; }

    [Column("generatedBy")]
    public int? GeneratedBy { get; set; }

    [Required]
    [Column("filePath")]
    public string FilePath { get; set; } = string.Empty;

    [Column("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("DepartmentId")]
    public Department? Department { get; set; }

    [ForeignKey("GeneratedBy")]
    public User? GeneratedByUser { get; set; }
}
