using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text;
using QRCoder;
using IoclFleetApi.Data;
using IoclFleetApi.DTOs;
using IoclFleetApi.Hubs;
using IoclFleetApi.Models;
using IoclFleetApi.Services;

namespace IoclFleetApi.Controllers;

[ApiController]
[Route("api/vehicles")]
public class VehiclesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly IComplianceService _compliance;
    private readonly IConfiguration _config;

    public VehiclesController(AppDbContext db, IAuditService audit, IComplianceService compliance, IConfiguration config)
    {
        _db = db;
        _audit = audit;
        _compliance = compliance;
        _config = config;
    }

    private (int id, string username, string role, int? departmentId) GetCurrentUser()
    {
        var id = int.Parse(User.FindFirst("id")!.Value);
        var username = User.FindFirst("username")!.Value;
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)!.Value;
        var user = _db.Users.Find(id);
        return (id, username, role, user?.DepartmentId);
    }

    /// <summary>Public gate verification — no auth required</summary>
    [HttpGet("verify/{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublicVehicleVerify(int id)
    {
        var vehicle = await _db.Vehicles
            .Include(v => v.Department)
            .Include(v => v.ComplianceRecords)
            .FirstOrDefaultAsync(v => v.Id == id);

        if (vehicle == null)
            return NotFound(ApiResponse.Fail("Vehicle not found."));

        return Ok(ApiResponse.Ok(new
        {
            vehicle.Id, vehicle.VehicleNumber, vehicle.VehicleType, vehicle.DepartmentId,
            vehicle.DriverName, vehicle.VendorName, vehicle.OverallStatus,
            vehicle.IsVerified, vehicle.VerifiedBy,
            vehicle.CreatedAt, vehicle.UpdatedAt,
            department = vehicle.Department != null ? new { vehicle.Department.Id, vehicle.Department.Name, vehicle.Department.Code, vehicle.Department.Division } : null,
            complianceRecords = vehicle.ComplianceRecords.Select(c => new
            {
                c.Id, c.VehicleId, c.LicenseType, c.LicenseNumber, c.IssuingAuthority,
                c.IssueDate, c.ExpiryDate, c.Status, c.IsVerified, c.VerifiedBy
            })
        }, "Public gate check credentials loaded."));
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetAllVehicles(
        [FromQuery] string? search, [FromQuery] int? departmentId,
        [FromQuery] string? status, [FromQuery] string? vehicleType)
    {
        var (_, _, role, userDeptId) = GetCurrentUser();
        var query = _db.Vehicles
            .Include(v => v.Department)
            .Include(v => v.RegistrationDocument)
            .AsQueryable();

        if (role == "DEPT_ADMIN")
            query = query.Where(v => v.DepartmentId == userDeptId);
        else if (departmentId.HasValue)
            query = query.Where(v => v.DepartmentId == departmentId.Value);

        if (!string.IsNullOrEmpty(search))
            query = query.Where(v =>
                v.VehicleNumber.Contains(search) ||
                (v.DriverName != null && v.DriverName.Contains(search)) ||
                (v.VendorName != null && v.VendorName.Contains(search)));

        if (!string.IsNullOrEmpty(status))
            query = query.Where(v => v.OverallStatus == status);

        if (!string.IsNullOrEmpty(vehicleType))
            query = query.Where(v => v.VehicleType == vehicleType);

        var vehicles = await query.OrderByDescending(v => v.CreatedAt).ToListAsync();

        // Project to clean DTOs — exclude qrCodeUrl (base64, ~10KB each) from list endpoint
        var result = vehicles.Select(v => new
        {
            v.Id, v.VehicleNumber, v.VehicleType, v.DepartmentId,
            v.DriverName, v.VendorName, v.OverallStatus, v.DocumentId,
            v.LastUpdatedBy, v.LastUpdatedTimestamp,
            v.IsVerified, v.VerifiedBy,
            v.CreatedAt, v.UpdatedAt,
            department = v.Department != null ? new { v.Department.Id, v.Department.Name, v.Department.Code, v.Department.Division } : null,
            registrationDocument = v.RegistrationDocument != null ? new { v.RegistrationDocument.Id, v.RegistrationDocument.FileName, v.RegistrationDocument.FilePath, v.RegistrationDocument.FileType } : null
        });

        return Ok(ApiResponse.Ok(result, "Vehicles retrieved successfully."));
    }

    [Authorize]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetVehicleById(int id)
    {
        var (_, _, role, userDeptId) = GetCurrentUser();
        var vehicle = await _db.Vehicles
            .Include(v => v.Department)
            .Include(v => v.RegistrationDocument)
            .Include(v => v.ComplianceRecords)
                .ThenInclude(c => c.Document)
            .FirstOrDefaultAsync(v => v.Id == id);

        if (vehicle == null)
            return NotFound(ApiResponse.Fail("Vehicle not found."));

        if (role == "DEPT_ADMIN" && vehicle.DepartmentId != userDeptId)
            return StatusCode(403, ApiResponse.Fail("Access denied. This vehicle belongs to another department."));

        // Return full detail including QR code and compliance records
        var result = new
        {
            vehicle.Id, vehicle.VehicleNumber, vehicle.VehicleType, vehicle.DepartmentId,
            vehicle.DriverName, vehicle.VendorName, vehicle.QrCodeUrl, vehicle.OverallStatus,
            vehicle.DocumentId, vehicle.LastUpdatedBy, vehicle.LastUpdatedTimestamp,
            vehicle.IsVerified, vehicle.VerifiedBy,
            vehicle.CreatedAt, vehicle.UpdatedAt,
            department = vehicle.Department != null ? new { vehicle.Department.Id, vehicle.Department.Name, vehicle.Department.Code, vehicle.Department.Division } : null,
            registrationDocument = vehicle.RegistrationDocument != null ? new { vehicle.RegistrationDocument.Id, vehicle.RegistrationDocument.FileName, vehicle.RegistrationDocument.FilePath, vehicle.RegistrationDocument.FileType } : null,
            complianceRecords = vehicle.ComplianceRecords.Select(c => new
            {
                c.Id, c.VehicleId, c.LicenseType, c.LicenseNumber, c.IssuingAuthority,
                c.IssueDate, c.ExpiryDate, c.Status, c.DocumentId,
                c.IsVerified, c.VerifiedBy,
                c.LastUpdatedBy, c.LastUpdatedTimestamp,
                c.CreatedAt, c.UpdatedAt,
                document = c.Document != null ? new { c.Document.Id, c.Document.FileName, c.Document.FilePath, c.Document.FileType } : null
            })
        };

        return Ok(ApiResponse.Ok(result, "Vehicle details loaded."));
    }

    [Authorize(Roles = "SUPER_ADMIN,DEPT_ADMIN")]
    [HttpPost]
    public async Task<IActionResult> CreateVehicle([FromForm] CreateVehicleDto dto, IFormCollection form)
    {
        var (userId, username, role, userDeptId) = GetCurrentUser();

        if (string.IsNullOrEmpty(dto.VehicleNumber) || string.IsNullOrEmpty(dto.VehicleType) || !dto.DepartmentId.HasValue)
            return BadRequest(ApiResponse.Fail("Vehicle number, vehicle type, and department are required."));

        var targetDeptId = dto.DepartmentId.Value;
        if (role == "DEPT_ADMIN" && userDeptId != targetDeptId)
            return StatusCode(403, ApiResponse.Fail("Access denied. You can only register vehicles in your own department."));

        var dept = await _db.Departments.FindAsync(targetDeptId);
        if (dept == null)
            return NotFound(ApiResponse.Fail("Department not found."));

        // Accept doc_RC (new multi-doc form) OR legacy 'file' field
        var rcFile = form.Files["doc_RC"] ?? form.Files["file"];
        if (rcFile == null)
            return BadRequest(ApiResponse.Fail("Registration Certificate (RC Copy) upload is mandatory."));

        // Check if duplicate vehicle number already exists
        var existing = await _db.Vehicles.FirstOrDefaultAsync(v => v.VehicleNumber == dto.VehicleNumber!.ToUpper().Replace(" ", ""));
        if (existing != null)
            return BadRequest(ApiResponse.Fail($"Vehicle {dto.VehicleNumber!.ToUpper()} is already registered in the system."));

        // Validate all 8 compliance documents are present in request and extract dates securely from PDFs
        var complianceTypes = new[] { "ROAD_PERMIT", "AGE_DETERMINATION", "PUC", "FITNESS", "EXPLOSIVE", "GREEN_CARD", "INSURANCE", "CALIBRATION" };
        var extractedExpiries = new Dictionary<string, DateTime>();

        foreach (var type in complianceTypes)
        {
            var compFile = form.Files[$"doc_{type}"];
            if (compFile == null)
                return BadRequest(ApiResponse.Fail($"Document upload for {type.Replace("_", " ")} is mandatory."));

            // Enforce that uploaded compliance files must be readable PDF files for security verification
            var isPdf = compFile.ContentType == "application/pdf" || compFile.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
            if (!isPdf)
            {
                return BadRequest(ApiResponse.Fail($"Document for {type.Replace("_", " ")} must be a readable PDF file. Scanned images are not permitted."));
            }

            var issueDateStr = form[$"issue_{type}"].FirstOrDefault();
            if (string.IsNullOrEmpty(issueDateStr) || !DateTime.TryParse(issueDateStr, out _))
                return BadRequest(ApiResponse.Fail($"A valid issue date is required for {type.Replace("_", " ")}."));

            // Securely extract date directly from the PDF file
            DateTime extractedExpiry;
            try
            {
                using var ms = new MemoryStream();
                await compFile.CopyToAsync(ms);
                var fileBytes = ms.ToArray();

                using var pdfDoc = UglyToad.PdfPig.PdfDocument.Open(fileBytes);
                var text = new StringBuilder();
                foreach (var page in pdfDoc.GetPages())
                    text.AppendLine(page.Text);

                var extractedDate = ComplianceController.ExtractExpiryDate(text.ToString());
                if (!extractedDate.HasValue)
                {
                    var manualExpiryStr = form[$"expiry_{type}"].FirstOrDefault();
                    if (DateTime.TryParse(manualExpiryStr, out var manualExpiry))
                    {
                        extractedExpiry = manualExpiry;
                    }
                    else
                    {
                        return BadRequest(ApiResponse.Fail($"Could not automatically extract a valid expiry date from the uploaded PDF for {type.Replace("_", " ")}. Please ensure the document contains a clear expiry date or enter it manually."));
                    }
                }
                else
                {
                    extractedExpiry = extractedDate.Value;
                }
                extractedExpiries[type] = extractedExpiry;
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse.Fail($"Failed to parse the PDF document for {type.Replace("_", " ")}: {ex.Message}"));
            }

            if (DateTime.TryParse(issueDateStr, out var parsedIssue))
            {
                if (extractedExpiry < parsedIssue)
                {
                    return BadRequest(ApiResponse.Fail($"Extracted expiry date ({extractedExpiry:yyyy-MM-dd}) must be after issue date for {type.Replace("_", " ")}."));
                }
            }

            var licNo = form[$"licNo_{type}"].FirstOrDefault();
            if (string.IsNullOrEmpty(licNo) || licNo == "PENDING")
                return BadRequest(ApiResponse.Fail($"License/Cert number is required for {type.Replace("_", " ")}."));
        }

        var uploadDir = Path.GetFullPath(_config["Upload:Directory"] ?? "./uploads");
        Directory.CreateDirectory(uploadDir);

        // Helper to save a single file and return a Document entity
        async Task<Document> SaveFile(IFormFile f)
        {
            var uniqueName = $"file-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Random.Shared.Next(1000000000)}{Path.GetExtension(f.FileName)}";
            var filePath = Path.Combine(uploadDir, uniqueName);
            using var stream = new FileStream(filePath, FileMode.Create);
            await f.CopyToAsync(stream);
            return new Document
            {
                FileName = f.FileName,
                FilePath = $"/uploads/{uniqueName}",
                FileType = f.ContentType,
                FileSize = (int)f.Length,
                UploadedBy = userId
            };
        }

        // Save RC document
        var rcDoc = await SaveFile(rcFile);
        _db.Documents.Add(rcDoc);
        await _db.SaveChangesAsync();

        // Create vehicle
        var vehicleNumber = dto.VehicleNumber!.ToUpper().Replace(" ", "");
        var vehicle = new Vehicle
        {
            VehicleNumber = vehicleNumber,
            VehicleType = dto.VehicleType,
            DriverName = dto.DriverName,
            VendorName = dto.VendorName,
            DepartmentId = targetDeptId,
            OverallStatus = "FULLY_COMPLIANT",
            DocumentId = rcDoc.Id,
            LastUpdatedBy = username,
            LastUpdatedTimestamp = DateTime.UtcNow
        };
        _db.Vehicles.Add(vehicle);
        await _db.SaveChangesAsync();

        // Generate QR Code
        var frontendUrl = _config["Frontend:Url"] ?? "http://localhost:5173";
        var verificationUrl = $"{frontendUrl}/verify/vehicle/{vehicle.Id}";
        try
        {
            using var qrGenerator = new QRCodeGenerator();
            var qrData = qrGenerator.CreateQrCode(verificationUrl, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrData);
            var pngBytes = qrCode.GetGraphic(10);
            vehicle.QrCodeUrl = $"data:image/png;base64,{Convert.ToBase64String(pngBytes)}";
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[VehicleController] QR Code Generation failed: {ex.Message}");
        }

        // Create compliance records — save doc if uploaded, otherwise mark PENDING
        // complianceTypes is already defined above
        int docsUploaded = 0;
        foreach (var type in complianceTypes)
        {
            var compFile = form.Files[$"doc_{type}"];
            var issueDateStr = form[$"issue_{type}"].FirstOrDefault();
            DateTime? issueDate = null;
            if (DateTime.TryParse(issueDateStr, out var parsedIssueDate))
                issueDate = parsedIssueDate;

            var expiryDate = extractedExpiries[type];

            int? compDocId = null;
            if (compFile != null)
            {
                var compDoc = await SaveFile(compFile);
                _db.Documents.Add(compDoc);
                await _db.SaveChangesAsync();
                compDocId = compDoc.Id;
                docsUploaded++;
            }

            _db.ComplianceRecords.Add(new ComplianceRecord
            {
                VehicleId = vehicle.Id,
                LicenseType = type,
                LicenseNumber = form[$"licNo_{type}"].FirstOrDefault() ?? "PENDING",
                IssuingAuthority = "PENDING",
                IssueDate = issueDate.HasValue ? issueDate.Value.ToString("yyyy-MM-dd") : null,
                ExpiryDate = expiryDate.ToString("yyyy-MM-dd"),
                Status = "ACTIVE",
                DocumentId = compDocId,
                LastUpdatedBy = username,
                LastUpdatedTimestamp = DateTime.UtcNow
            });
        }
        await _db.SaveChangesAsync();

        // Re-evaluate overall status based on any real expiry dates provided
        await _compliance.UpdateVehicleStatus(vehicle.Id);

        await _audit.LogAction(userId, username, "CREATE_VEHICLE",
            $"Registered vehicle {vehicle.VehicleNumber} under {dept.Name}. RC uploaded. {docsUploaded}/8 compliance documents uploaded.",
            departmentId: targetDeptId, vehicleId: vehicle.Id,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        return StatusCode(201, ApiResponse.Ok(vehicle, $"Vehicle registered successfully. {docsUploaded}/8 compliance documents uploaded. Remaining can be added via Renewal."));
    }

    [Authorize(Roles = "SUPER_ADMIN,DEPT_ADMIN")]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateVehicle(int id, [FromBody] UpdateVehicleDto dto)
    {
        var (userId, username, role, userDeptId) = GetCurrentUser();
        var vehicle = await _db.Vehicles.FindAsync(id);
        if (vehicle == null) return NotFound(ApiResponse.Fail("Vehicle not found."));

        if (role == "DEPT_ADMIN" && vehicle.DepartmentId != userDeptId)
            return StatusCode(403, ApiResponse.Fail("Access denied. You cannot modify other departments' vehicles."));

        var oldValue = new { vehicle.VehicleType, vehicle.DriverName, vehicle.VendorName, vehicle.DepartmentId };

        if (!string.IsNullOrEmpty(dto.VehicleType)) vehicle.VehicleType = dto.VehicleType;
        if (dto.DriverName != null) vehicle.DriverName = dto.DriverName;
        if (dto.VendorName != null) vehicle.VendorName = dto.VendorName;
        vehicle.LastUpdatedBy = username;
        vehicle.LastUpdatedTimestamp = DateTime.UtcNow;

        if (dto.DepartmentId.HasValue && role == "SUPER_ADMIN")
        {
            var deptExists = await _db.Departments.FindAsync(dto.DepartmentId.Value);
            if (deptExists != null) vehicle.DepartmentId = dto.DepartmentId.Value;
        }

        await _db.SaveChangesAsync();

        await _audit.LogAction(userId, username, "UPDATE_VEHICLE",
            $"Updated vehicle metadata for {vehicle.VehicleNumber}.",
            oldValue: oldValue, departmentId: vehicle.DepartmentId,
            vehicleId: vehicle.Id, ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(ApiResponse.Ok(vehicle, "Vehicle details updated successfully."));
    }

    [Authorize(Roles = "SUPER_ADMIN,DEPT_ADMIN")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteVehicle(int id)
    {
        var (userId, username, role, userDeptId) = GetCurrentUser();
        var vehicle = await _db.Vehicles.FindAsync(id);
        if (vehicle == null) return NotFound(ApiResponse.Fail("Vehicle not found."));

        if (role == "DEPT_ADMIN" && vehicle.DepartmentId != userDeptId)
            return StatusCode(403, ApiResponse.Fail("Access denied. You cannot delete other departments' vehicles."));

        var oldDeptId = vehicle.DepartmentId;
        _db.Vehicles.Remove(vehicle);
        await _db.SaveChangesAsync();

        try { await _compliance.UpdateDepartmentScore(oldDeptId); } catch { /* ignore */ }

        await _audit.LogAction(userId, username, "DELETE_VEHICLE",
            $"Deleted vehicle {vehicle.VehicleNumber}.",
            departmentId: oldDeptId, vehicleId: id,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(ApiResponse.Ok(null, "Vehicle deleted successfully."));
    }

    [Authorize(Roles = "SUPER_ADMIN")]
    [HttpPut("{id}/verify")]
    public async Task<IActionResult> VerifyVehicleDocument(int id, [FromBody] VerifyDocumentDto dto)
    {
        var (userId, username, _, _) = GetCurrentUser();
        var vehicle = await _db.Vehicles.FindAsync(id);
        if (vehicle == null) return NotFound(ApiResponse.Fail("Vehicle not found."));

        vehicle.IsVerified = dto.IsVerified;
        vehicle.VerifiedBy = dto.IsVerified ? username : null;
        await _db.SaveChangesAsync();

        await _audit.LogAction(userId, username, "VERIFY_VEHICLE_RC",
            $"{(dto.IsVerified ? "Verified" : "Revoked verification of")} RC document for vehicle {vehicle.VehicleNumber}.",
            departmentId: vehicle.DepartmentId, vehicleId: vehicle.Id,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(ApiResponse.Ok(vehicle, "Vehicle RC verification status updated successfully."));
    }

    [HttpGet("verify/plate/{plateNumber}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublicVehicleVerifyByPlate(string plateNumber)
    {
        if (string.IsNullOrWhiteSpace(plateNumber))
            return BadRequest(ApiResponse.Fail("Plate number is required."));

        var normalizedPlate = plateNumber.ToUpper().Replace(" ", "").Replace("-", "");

        var vehicle = await _db.Vehicles
            .Include(v => v.Department)
            .Include(v => v.ComplianceRecords)
            .FirstOrDefaultAsync(v => v.VehicleNumber.ToUpper().Replace(" ", "").Replace("-", "") == normalizedPlate);

        if (vehicle == null)
            return NotFound(ApiResponse.Fail($"Vehicle with plate number {plateNumber} not found."));

        return Ok(ApiResponse.Ok(new
        {
            vehicle.Id, vehicle.VehicleNumber, vehicle.VehicleType, vehicle.DepartmentId,
            vehicle.DriverName, vehicle.VendorName, vehicle.OverallStatus,
            vehicle.IsVerified, vehicle.VerifiedBy,
            vehicle.CreatedAt, vehicle.UpdatedAt,
            department = vehicle.Department != null ? new { vehicle.Department.Id, vehicle.Department.Name, vehicle.Department.Code, vehicle.Department.Division } : null,
            complianceRecords = vehicle.ComplianceRecords.Select(c => new
            {
                c.Id, c.VehicleId, c.LicenseType, c.LicenseNumber, c.IssuingAuthority,
                c.IssueDate, c.ExpiryDate, c.Status, c.IsVerified, c.VerifiedBy
            })
        }, "Public gate check credentials loaded."));
    }

    [Authorize(Roles = "GATEMAN,SUPER_ADMIN")]
    [HttpPost("gate-entry/log")]
    public async Task<IActionResult> LogGateEntry([FromBody] GateEntryLogDto dto)
    {
        var (userId, username, _, _) = GetCurrentUser();
        var vehicle = await _db.Vehicles.FindAsync(dto.VehicleId);
        if (vehicle == null) return NotFound(ApiResponse.Fail("Vehicle not found."));

        var action = dto.Allowed ? "GATE_ENTRY_ALLOW" : "GATE_ENTRY_DENY";
        var description = $"Gateman {username} {(dto.Allowed ? "allowed" : "denied")} entry for vehicle {vehicle.VehicleNumber}. Reason/Remarks: {dto.Remarks ?? "None"}";

        await _audit.LogAction(userId, username, action, description, 
            departmentId: vehicle.DepartmentId, vehicleId: vehicle.Id,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(ApiResponse.Ok(null, $"Gate entry {(dto.Allowed ? "allowed" : "denied")} logged successfully."));
    }
}
