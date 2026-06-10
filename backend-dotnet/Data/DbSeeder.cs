using QRCoder;
using IoclFleetApi.Models;

namespace IoclFleetApi.Data;

/// <summary>
/// Database seeder mirroring seed.js — creates 6 departments, 8 users, 100 vehicles, 800 compliance records
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, IConfiguration config)
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("IOCL Panipat Refinery - Fleet Compliance Seeding");
        Console.WriteLine("=================================================");

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        Console.WriteLine("[✓] Database synchronized (tables dropped and recreated).");

        // 1. DEPARTMENTS
        var deptsData = new[]
        {
            new Department { Name = "PR - Fire & Safety", Code = "PR-FS", Description = "Panipat Refinery: Fire Prevention, Emergency Response, Fire Tenders and Safety Equipment Fleet.", Division = "Panipat Refinery", ComplianceScore = 96.5 },
            new Department { Name = "PR - Refinery Operations", Code = "PR-OPS", Description = "Panipat Refinery: Crude Oil Refining, Main Process Units, LPG/HSD Tanker Fleet.", Division = "Panipat Refinery", ComplianceScore = 89.0 },
            new Department { Name = "PR - Chemical & Laboratory", Code = "PR-CHEM", Description = "Panipat Refinery: Quality Control Labs, Chemical Sampling, Catalyst & Additive Fleet.", Division = "Panipat Refinery", ComplianceScore = 92.3 },
            new Department { Name = "PNC - Fire & Safety", Code = "PNC-FS", Description = "Panipat Naphtha Cracker: Emergency Response Unit, Fire Tender and Safety Fleet.", Division = "Panipat Naphtha Cracker", ComplianceScore = 97.8 },
            new Department { Name = "PNC - Cracker Operations", Code = "PNC-OPS", Description = "Panipat Naphtha Cracker: Ethylene/Propylene Production, Polymer & Naphtha Tanker Fleet.", Division = "Panipat Naphtha Cracker", ComplianceScore = 84.5 },
            new Department { Name = "PNC - Chemical & Testing", Code = "PNC-CHEM", Description = "Panipat Naphtha Cracker: Polymer QC, Feedstock Testing, Chemical Carrier Fleet.", Division = "Panipat Naphtha Cracker", ComplianceScore = 91.0 }
        };
        db.Departments.AddRange(deptsData);
        await db.SaveChangesAsync();
        Console.WriteLine($"[✓] Seeded {deptsData.Length} departments (3 PR + 3 PNC).");

        var prFS = deptsData[0]; var prOPS = deptsData[1]; var prCHEM = deptsData[2];
        var pncFS = deptsData[3]; var pncOPS = deptsData[4]; var pncCHEM = deptsData[5];

        // 2. USERS
        var passwordHash = BCrypt.Net.BCrypt.HashPassword("password123");
        var usersData = new[]
        {
            new User { Username = "superadmin", Email = "singhanubhav1562@gmail.com", Password = passwordHash, Role = "SUPER_ADMIN", DepartmentId = null, Status = "ACTIVE" },
            new User { Username = "gateman", Email = "singhanubhav1562@gmail.com", Password = passwordHash, Role = "GATEMAN", DepartmentId = null, Status = "ACTIVE" },
            new User { Username = "prfsadmin", Email = "singhanubhav1562@gmail.com", Password = passwordHash, Role = "DEPT_ADMIN", DepartmentId = prFS.Id, Status = "ACTIVE" },
            new User { Username = "propsadmin", Email = "singhanubhav1562@gmail.com", Password = passwordHash, Role = "DEPT_ADMIN", DepartmentId = prOPS.Id, Status = "ACTIVE" },
            new User { Username = "prchemadmin", Email = "singhanubhav1562@gmail.com", Password = passwordHash, Role = "DEPT_ADMIN", DepartmentId = prCHEM.Id, Status = "ACTIVE" },
            new User { Username = "pncfsadmin", Email = "singhanubhav1562@gmail.com", Password = passwordHash, Role = "DEPT_ADMIN", DepartmentId = pncFS.Id, Status = "ACTIVE" },
            new User { Username = "pncopsadmin", Email = "singhanubhav1562@gmail.com", Password = passwordHash, Role = "DEPT_ADMIN", DepartmentId = pncOPS.Id, Status = "ACTIVE" },
            new User { Username = "pncchemadmin", Email = "singhanubhav1562@gmail.com", Password = passwordHash, Role = "DEPT_ADMIN", DepartmentId = pncCHEM.Id, Status = "ACTIVE" },
            new User { Username = "complianceviewer", Email = "singhanubhav1562@gmail.com", Password = passwordHash, Role = "VIEWER", DepartmentId = null, Status = "ACTIVE" }
        };
        db.Users.AddRange(usersData);
        await db.SaveChangesAsync();
        Console.WriteLine($"[✓] Seeded {usersData.Length} users.");

        // 3. VEHICLES & COMPLIANCE RECORDS FOR OPERATIONAL PRESENTATION USE
        var dummyDoc = new Document
        {
            FileName = "seeded_document.pdf",
            FilePath = "/uploads/seeded/iocl_sample_doc.pdf",
            FileType = "application/pdf",
            FileSize = 1024,
            UploadedBy = usersData[0].Id
        };
        db.Documents.Add(dummyDoc);
        await db.SaveChangesAsync();

        var depts = new[] { prFS, prOPS, prCHEM, pncFS, pncOPS, pncCHEM };
        var complianceTypes = new[] { "ROAD_PERMIT", "AGE_DETERMINATION", "PUC", "FITNESS", "EXPLOSIVE", "GREEN_CARD", "INSURANCE", "CALIBRATION" };
        
        foreach (var dept in depts)
        {
            // 1. Fully Compliant Vehicle
            var vA = new Vehicle
            {
                VehicleNumber = $"HR26AB110{dept.Id}",
                VehicleType = "Petroleum Tanker",
                DriverName = $"Safe Driver {dept.Code}",
                VendorName = "Refinery Carrier Corp",
                DepartmentId = dept.Id,
                OverallStatus = "FULLY_COMPLIANT",
                DocumentId = dummyDoc.Id,
                LastUpdatedBy = "system",
                LastUpdatedTimestamp = DateTime.UtcNow
            };
            db.Vehicles.Add(vA);
            await db.SaveChangesAsync();

            // Generate QR Code base64
            var verificationUrlA = $"http://localhost:5173/verify/vehicle/{vA.Id}";
            try
            {
                using var qrGen = new QRCodeGenerator();
                var qrData = qrGen.CreateQrCode(verificationUrlA, QRCodeGenerator.ECCLevel.Q);
                using var qrCode = new PngByteQRCode(qrData);
                vA.QrCodeUrl = $"data:image/png;base64,{Convert.ToBase64String(qrCode.GetGraphic(10))}";
            }
            catch {}

            foreach (var type in complianceTypes)
            {
                db.ComplianceRecords.Add(new ComplianceRecord
                {
                    VehicleId = vA.Id,
                    LicenseType = type,
                    LicenseNumber = $"LIC-{type}-{Random.Shared.Next(1000, 9999)}",
                    IssuingAuthority = "Govt of India",
                    IssueDate = "2026-06-01",
                    ExpiryDate = "2028-06-01", // Far in the future
                    Status = "ACTIVE",
                    DocumentId = dummyDoc.Id,
                    LastUpdatedBy = "system",
                    LastUpdatedTimestamp = DateTime.UtcNow
                });
            }

            // 2. Near Expiry Vehicle
            var vB = new Vehicle
            {
                VehicleNumber = $"HR26AB990{dept.Id}",
                VehicleType = "Cargo Truck",
                DriverName = $"Alert Driver {dept.Code}",
                VendorName = "Refinery Carrier Corp",
                DepartmentId = dept.Id,
                OverallStatus = "WARNING",
                DocumentId = dummyDoc.Id,
                LastUpdatedBy = "system",
                LastUpdatedTimestamp = DateTime.UtcNow
            };
            db.Vehicles.Add(vB);
            await db.SaveChangesAsync();

            var verificationUrlB = $"http://localhost:5173/verify/vehicle/{vB.Id}";
            try
            {
                using var qrGen = new QRCodeGenerator();
                var qrData = qrGen.CreateQrCode(verificationUrlB, QRCodeGenerator.ECCLevel.Q);
                using var qrCode = new PngByteQRCode(qrData);
                vB.QrCodeUrl = $"data:image/png;base64,{Convert.ToBase64String(qrCode.GetGraphic(10))}";
            }
            catch {}

            foreach (var type in complianceTypes)
            {
                var isExpiringSoon = type == "PUC";
                var expiryDate = isExpiringSoon 
                    ? DateTime.Today.AddDays(3).ToString("yyyy-MM-dd") 
                    : "2028-06-01";
                var status = isExpiringSoon ? "WARNING" : "ACTIVE";

                db.ComplianceRecords.Add(new ComplianceRecord
                {
                    VehicleId = vB.Id,
                    LicenseType = type,
                    LicenseNumber = $"LIC-{type}-{Random.Shared.Next(1000, 9999)}",
                    IssuingAuthority = "Govt of India",
                    IssueDate = "2026-06-01",
                    ExpiryDate = expiryDate,
                    Status = status,
                    DocumentId = dummyDoc.Id,
                    LastUpdatedBy = "system",
                    LastUpdatedTimestamp = DateTime.UtcNow
                });
            }
        }
        await db.SaveChangesAsync();
        Console.WriteLine("[✓] Seeded vehicles & compliance records (2 per department).");


        // 6. AUDIT LOG
        var superAdmin = usersData[0];
        db.AuditLogs.Add(new AuditLog
        {
            UserId = superAdmin.Id, Username = "superadmin",
            Action = "DATABASE_INITIALIZATION",
            Description = $"IOCL Panipat Refinery Fleet Compliance DB seeded successfully. 6 departments, {usersData.Length} users seeded.",
            IpAddress = "127.0.0.1"
        });
        await db.SaveChangesAsync();

        Console.WriteLine("[✓] Initial audit trail recorded.");
        Console.WriteLine("\n=== Seeding Complete! ===\n");
        Console.WriteLine("Available Login Credentials (password: password123):");
        Console.WriteLine("  Super Admin   : superadmin");
        Console.WriteLine("  Gateman       : gateman");
        Console.WriteLine("  PR Fire&Safety: prfsadmin");
        Console.WriteLine("  PR Operations : propsadmin");
        Console.WriteLine("  PR Chemical   : prchemadmin");
        Console.WriteLine("  PNC Fire&Safety: pncfsadmin");
        Console.WriteLine("  PNC Cracker   : pncopsadmin");
        Console.WriteLine("  PNC Chemical  : pncchemadmin");
        Console.WriteLine("  Viewer        : complianceviewer");
        Console.WriteLine("===========================");
    }
}
