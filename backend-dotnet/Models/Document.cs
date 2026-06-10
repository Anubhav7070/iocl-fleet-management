using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoclFleetApi.Models;

[Table("documents")]
public class Document
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("fileName")]
    public string FileName { get; set; } = string.Empty;

    [Required]
    [Column("filePath")]
    public string FilePath { get; set; } = string.Empty;

    [Column("fileType")]
    public string? FileType { get; set; }

    [Column("fileSize")]
    public int? FileSize { get; set; }

    [Column("uploadedBy")]
    public int? UploadedBy { get; set; }

    [Column("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("UploadedBy")]
    public User? Uploader { get; set; }

    public ICollection<ComplianceRecord> ComplianceRecords { get; set; } = new List<ComplianceRecord>();
    public ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
}
