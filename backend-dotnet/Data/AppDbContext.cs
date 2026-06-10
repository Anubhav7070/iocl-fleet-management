using Microsoft.EntityFrameworkCore;
using IoclFleetApi.Models;

namespace IoclFleetApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Department> Departments => Set<Department>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<ComplianceRecord> ComplianceRecords => Set<ComplianceRecord>();
    public DbSet<RenewalHistory> RenewalHistories => Set<RenewalHistory>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Report> Reports => Set<Report>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Unique indexes
        modelBuilder.Entity<Department>().HasIndex(d => d.Name).IsUnique();
        modelBuilder.Entity<Department>().HasIndex(d => d.Code).IsUnique();
        modelBuilder.Entity<User>().HasIndex(u => u.Username).IsUnique();
        modelBuilder.Entity<User>().HasIndex(u => u.Email);
        modelBuilder.Entity<Vehicle>().HasIndex(v => v.VehicleNumber).IsUnique();

        // Department & User
        modelBuilder.Entity<User>()
            .HasOne(u => u.Department)
            .WithMany(d => d.Users)
            .HasForeignKey(u => u.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        // Department & Vehicle
        modelBuilder.Entity<Vehicle>()
            .HasOne(v => v.Department)
            .WithMany(d => d.Vehicles)
            .HasForeignKey(v => v.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Vehicle & ComplianceRecord
        modelBuilder.Entity<ComplianceRecord>()
            .HasOne(c => c.Vehicle)
            .WithMany(v => v.ComplianceRecords)
            .HasForeignKey(c => c.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        // ComplianceRecord & Document
        modelBuilder.Entity<ComplianceRecord>()
            .HasOne(c => c.Document)
            .WithMany(d => d.ComplianceRecords)
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.SetNull);

        // User & Document (upload tracking)
        modelBuilder.Entity<Document>()
            .HasOne(d => d.Uploader)
            .WithMany(u => u.Documents)
            .HasForeignKey(d => d.UploadedBy)
            .OnDelete(DeleteBehavior.SetNull);

        // Vehicle & Notification
        modelBuilder.Entity<Notification>()
            .HasOne(n => n.Vehicle)
            .WithMany(v => v.Notifications)
            .HasForeignKey(n => n.VehicleId)
            .OnDelete(DeleteBehavior.SetNull);

        // Department & Notification
        modelBuilder.Entity<Notification>()
            .HasOne(n => n.Department)
            .WithMany(d => d.Notifications)
            .HasForeignKey(n => n.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        // Vehicle & RenewalHistory
        modelBuilder.Entity<RenewalHistory>()
            .HasOne(r => r.Vehicle)
            .WithMany(v => v.Renewals)
            .HasForeignKey(r => r.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        // ComplianceRecord & RenewalHistory
        modelBuilder.Entity<RenewalHistory>()
            .HasOne(r => r.ComplianceRecord)
            .WithMany(c => c.Renewals)
            .HasForeignKey(r => r.ComplianceRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        // RenewalHistory & Document (old & new)
        modelBuilder.Entity<RenewalHistory>()
            .HasOne(r => r.OldDocument)
            .WithMany()
            .HasForeignKey(r => r.OldDocumentId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<RenewalHistory>()
            .HasOne(r => r.NewDocument)
            .WithMany()
            .HasForeignKey(r => r.NewDocumentId)
            .OnDelete(DeleteBehavior.SetNull);

        // RenewalHistory & User
        modelBuilder.Entity<RenewalHistory>()
            .HasOne(r => r.RenewedByUser)
            .WithMany(u => u.Renewals)
            .HasForeignKey(r => r.RenewedBy)
            .OnDelete(DeleteBehavior.SetNull);

        // User & AuditLog
        modelBuilder.Entity<AuditLog>()
            .HasOne(a => a.User)
            .WithMany(u => u.AuditLogs)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Department & AuditLog
        modelBuilder.Entity<AuditLog>()
            .HasOne(a => a.DepartmentObject)
            .WithMany(d => d.AuditLogs)
            .HasForeignKey(a => a.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        // Vehicle & AuditLog
        modelBuilder.Entity<AuditLog>()
            .HasOne(a => a.Vehicle)
            .WithMany(v => v.AuditLogs)
            .HasForeignKey(a => a.VehicleId)
            .OnDelete(DeleteBehavior.SetNull);

        // Vehicle & Document (registration document)
        modelBuilder.Entity<Vehicle>()
            .HasOne(v => v.RegistrationDocument)
            .WithMany(d => d.Vehicles)
            .HasForeignKey(v => v.DocumentId)
            .OnDelete(DeleteBehavior.SetNull);

        // User & Report
        modelBuilder.Entity<Report>()
            .HasOne(r => r.GeneratedByUser)
            .WithMany(u => u.Reports)
            .HasForeignKey(r => r.GeneratedBy)
            .OnDelete(DeleteBehavior.SetNull);

        // Department & Report
        modelBuilder.Entity<Report>()
            .HasOne(r => r.Department)
            .WithMany(d => d.Reports)
            .HasForeignKey(r => r.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(ct);
    }

    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            var updatedAtProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "UpdatedAt");
            if (updatedAtProp != null)
            {
                updatedAtProp.CurrentValue = DateTime.UtcNow;
            }
        }
    }
}
