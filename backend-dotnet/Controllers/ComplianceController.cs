using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using IoclFleetApi.Data;
using IoclFleetApi.DTOs;
using IoclFleetApi.Hubs;
using IoclFleetApi.Models;
using IoclFleetApi.Services;

namespace IoclFleetApi.Controllers;

[ApiController]
[Route("api/compliance")]
[Authorize]
public class ComplianceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly IComplianceService _compliance;
    private readonly IHubContext<ComplianceHub> _hub;
    private readonly IConfiguration _config;
    private readonly IComplianceAlertDispatcher _dispatcher;

    public ComplianceController(AppDbContext db, IAuditService audit, IComplianceService compliance,
        IHubContext<ComplianceHub> hub, IConfiguration config, IComplianceAlertDispatcher dispatcher)
    {
        _db = db;
        _audit = audit;
        _compliance = compliance;
        _hub = hub;
        _config = config;
        _dispatcher = dispatcher;
    }

    private (int id, string username, string role, int? departmentId) GetCurrentUser()
    {
        var id = int.Parse(User.FindFirst("id")!.Value);
        var username = User.FindFirst("username")!.Value;
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)!.Value;
        var user = _db.Users.Find(id);
        return (id, username, role, user?.DepartmentId);
    }

    [HttpGet]
    public async Task<IActionResult> GetComplianceRecords([FromQuery] int? vehicleId)
    {
        var (_, _, role, userDeptId) = GetCurrentUser();
        var query = _db.ComplianceRecords
            .Include(c => c.Vehicle)
            .Include(c => c.Document)
            .AsQueryable();

        if (role == "DEPT_ADMIN")
            query = query.Where(c => c.Vehicle!.DepartmentId == userDeptId);
        else if (vehicleId.HasValue)
            query = query.Where(c => c.VehicleId == vehicleId.Value);

        var records = await query.ToListAsync();
        var result = records.Select(c => new
        {
            c.Id, c.VehicleId, c.LicenseType, c.LicenseNumber, c.IssuingAuthority,
            c.IssueDate, c.ExpiryDate, c.Status, c.DocumentId,
            c.IsVerified, c.VerifiedBy, c.LastUpdatedBy, c.LastUpdatedTimestamp,
            c.CreatedAt, c.UpdatedAt,
            vehicle = c.Vehicle != null ? new { c.Vehicle.Id, c.Vehicle.VehicleNumber, c.Vehicle.VehicleType, c.Vehicle.DepartmentId } : null,
            document = c.Document != null ? new { c.Document.Id, c.Document.FileName, c.Document.FilePath } : null
        });
        return Ok(ApiResponse.Ok(result, "Compliance records loaded."));
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetRenewalHistory([FromQuery] int? vehicleId)
    {
        var (_, _, role, userDeptId) = GetCurrentUser();
        var query = _db.RenewalHistories
            .Include(r => r.Vehicle)
            .Include(r => r.OldDocument)
            .Include(r => r.NewDocument)
            .AsQueryable();

        if (role == "DEPT_ADMIN")
            query = query.Where(r => r.Vehicle!.DepartmentId == userDeptId);
        else if (vehicleId.HasValue)
            query = query.Where(r => r.VehicleId == vehicleId.Value);

        var history = await query.OrderByDescending(r => r.RenewedAt).ToListAsync();
        var result = history.Select(r => new
        {
            r.Id, r.ComplianceRecordId, r.VehicleId, r.LicenseType,
            r.OldExpiryDate, r.NewExpiryDate, r.RenewedBy, r.RenewedAt,
            r.OldDocumentId, r.NewDocumentId, r.CreatedAt,
            vehicle = r.Vehicle != null ? new { r.Vehicle.Id, r.Vehicle.VehicleNumber, r.Vehicle.VehicleType } : null,
            oldDocument = r.OldDocument != null ? new { r.OldDocument.Id, r.OldDocument.FileName, r.OldDocument.FilePath } : null,
            newDocument = r.NewDocument != null ? new { r.NewDocument.Id, r.NewDocument.FileName, r.NewDocument.FilePath } : null
        });
        return Ok(ApiResponse.Ok(result, "Renewal history retrieved."));
    }

    [HttpGet("alerts")]
    public async Task<IActionResult> GetComplianceAlerts([FromQuery] int? departmentId, [FromQuery] string? priority)
    {
        var (_, _, role, userDeptId) = GetCurrentUser();
        var activeDeptId = role == "DEPT_ADMIN" ? userDeptId : departmentId;

        var query = _db.ComplianceRecords
            .Include(c => c.Vehicle!)
            .ThenInclude(v => v.Department)
            .Where(c => c.Status != "ACTIVE")
            .AsQueryable();

        if (activeDeptId.HasValue)
            query = query.Where(c => c.Vehicle!.DepartmentId == activeDeptId.Value);

        if (!string.IsNullOrEmpty(priority))
        {
            query = priority switch
            {
                "HIGH" => query.Where(c => c.Status == "EXPIRED" || c.Status == "HIGH_CRITICAL"),
                "MEDIUM" => query.Where(c => c.Status == "MEDIUM_CRITICAL"),
                "LOW" => query.Where(c => c.Status == "WARNING"),
                _ => query
            };
        }

        var alerts = await query
            .OrderByDescending(c => c.Status == "EXPIRED" ? 0 :
                c.Status == "HIGH_CRITICAL" ? 1 :
                c.Status == "MEDIUM_CRITICAL" ? 2 :
                c.Status == "WARNING" ? 3 : 4)
            .ThenBy(c => c.ExpiryDate)
            .ToListAsync();

        var result = alerts.Select(c => new
        {
            c.Id, c.VehicleId, c.LicenseType, c.LicenseNumber, c.IssuingAuthority,
            c.IssueDate, c.ExpiryDate, c.Status, c.DocumentId,
            c.IsVerified, c.VerifiedBy, c.LastUpdatedBy, c.LastUpdatedTimestamp,
            c.CreatedAt, c.UpdatedAt,
            vehicle = c.Vehicle != null ? new
            {
                c.Vehicle.Id, c.Vehicle.VehicleNumber, c.Vehicle.VehicleType, c.Vehicle.DepartmentId,
                department = c.Vehicle.Department != null ? new { c.Vehicle.Department.Id, c.Vehicle.Department.Name, c.Vehicle.Department.Code } : null
            } : null
        });

        return Ok(ApiResponse.Ok(result, "Compliance alerts retrieved."));
    }

    [Authorize(Roles = "SUPER_ADMIN,DEPT_ADMIN")]
    [HttpPut("renew/{id}")]
    public async Task<IActionResult> RenewRecord(int id, [FromForm] RenewComplianceDto dto, IFormFile? file)
    {
        var (userId, username, role, userDeptId) = GetCurrentUser();

        if (file == null)
            return BadRequest(ApiResponse.Fail("Document upload is mandatory for all compliance entries."));

        // Enforce that uploaded documents must be readable PDF files for security verification
        var isPdf = file.ContentType == "application/pdf" || file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        if (!isPdf)
        {
            return BadRequest(ApiResponse.Fail("To prevent compliance tampering, all uploaded documents must be readable PDF files. Scanned images are not permitted."));
        }

        var record = await _db.ComplianceRecords
            .Include(c => c.Vehicle)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (record?.Vehicle == null)
            return NotFound(ApiResponse.Fail("Compliance record or associated vehicle not found."));

        if (role == "DEPT_ADMIN" && record.Vehicle.DepartmentId != userDeptId)
            return StatusCode(403, ApiResponse.Fail("Access denied. You cannot renew records for other departments."));

        // Save uploaded file to memory first to extract date, then save to disk
        string extractedDateStr = "";
        try
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var fileBytes = ms.ToArray();

            using var pdfDoc = PdfDocument.Open(fileBytes);
            var text = new StringBuilder();
            foreach (var page in pdfDoc.GetPages())
                text.AppendLine(page.Text);

            var extractedDate = ExtractExpiryDate(text.ToString());
            if (!extractedDate.HasValue)
            {
                return BadRequest(ApiResponse.Fail("Could not automatically extract a valid expiry date from the uploaded PDF. Please upload a PDF containing a clear expiry date."));
            }

            extractedDateStr = extractedDate.Value.ToString("yyyy-MM-dd");
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse.Fail($"Failed to parse the PDF document: {ex.Message}"));
        }

        var oldExpiryDate = record.ExpiryDate;
        var oldDocumentId = record.DocumentId;

        // Save uploaded file to disk
        var uploadDir = Path.GetFullPath(_config["Upload:Directory"] ?? "./uploads");
        Directory.CreateDirectory(uploadDir);
        var uniqueName = $"file-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Random.Shared.Next(1000000000)}{Path.GetExtension(file.FileName)}";
        var filePath = Path.Combine(uploadDir, uniqueName);
        
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            // Reset position of files or save using standard copy
            await file.CopyToAsync(stream);
        }

        var doc = new Document
        {
            FileName = file.FileName,
            FilePath = $"/uploads/{uniqueName}",
            FileType = file.ContentType,
            FileSize = (int)file.Length,
            UploadedBy = userId
        };
        _db.Documents.Add(doc);
        await _db.SaveChangesAsync();

        // Update compliance record using securely extracted date
        if (!string.IsNullOrEmpty(dto.LicenseNumber)) record.LicenseNumber = dto.LicenseNumber;
        if (!string.IsNullOrEmpty(dto.IssuingAuthority)) record.IssuingAuthority = dto.IssuingAuthority;
        if (!string.IsNullOrEmpty(dto.IssueDate)) record.IssueDate = dto.IssueDate;
        record.ExpiryDate = extractedDateStr;
        record.Status = _compliance.CalculateStatus(extractedDateStr);
        record.DocumentId = doc.Id;
        record.LastUpdatedBy = username;
        record.LastUpdatedTimestamp = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _compliance.UpdateVehicleStatus(record.VehicleId);

        var renewalHistory = new RenewalHistory
        {
            ComplianceRecordId = record.Id,
            VehicleId = record.VehicleId,
            LicenseType = record.LicenseType,
            OldExpiryDate = oldExpiryDate,
            NewExpiryDate = extractedDateStr,
            OldDocumentId = oldDocumentId,
            NewDocumentId = doc.Id,
            RenewedBy = userId
        };
        _db.RenewalHistories.Add(renewalHistory);
        await _db.SaveChangesAsync();

        // Emit real-time notification
        var payload = new
        {
            id = record.Id,
            vehicleNumber = record.Vehicle.VehicleNumber,
            licenseType = record.LicenseType,
            status = record.Status,
            expiryDate = record.ExpiryDate,
            updatedBy = username
        };
        await _hub.Clients.Group($"dept-{record.Vehicle.DepartmentId}").SendAsync("compliance_renewed", payload);
        await _hub.Clients.Group("super-admins").SendAsync("compliance_renewed", payload);

        _dispatcher.DispatchComplianceRenewed(payload);

        await _audit.LogAction(userId, username, "RENEW_DOCUMENT",
            $"Renewed {record.LicenseType} compliance certificate for {record.Vehicle.VehicleNumber}.",
            departmentId: record.Vehicle.DepartmentId, vehicleId: record.VehicleId,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(ApiResponse.Ok(new { record, renewalHistory }, "Compliance certificate renewed successfully."));
    }

    [Authorize(Roles = "SUPER_ADMIN")]
    [HttpPut("{id}/verify")]
    public async Task<IActionResult> VerifyComplianceDocument(int id, [FromBody] VerifyDocumentDto dto)
    {
        var (userId, username, _, _) = GetCurrentUser();

        var record = await _db.ComplianceRecords
            .Include(c => c.Vehicle)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (record == null) return NotFound(ApiResponse.Fail("Compliance record not found."));

        record.IsVerified = dto.IsVerified;
        record.VerifiedBy = dto.IsVerified ? username : null;
        await _db.SaveChangesAsync();

        await _audit.LogAction(userId, username, "VERIFY_COMPLIANCE_DOC",
            $"{(dto.IsVerified ? "Verified" : "Revoked verification of")} {record.LicenseType} certificate for vehicle {record.Vehicle?.VehicleNumber ?? "N/A"}.",
            departmentId: record.Vehicle?.DepartmentId, vehicleId: record.VehicleId,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(ApiResponse.Ok(record, "Compliance document verification status updated successfully."));
    }

    // ── Date Extraction from uploaded document ────────────────────────

    [HttpPost("extract-date")]
    [Authorize(Roles = "SUPER_ADMIN,DEPT_ADMIN")]
    public async Task<IActionResult> ExtractDateFromDocument(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse.Fail("No file uploaded."));

        try
        {
            var text = new StringBuilder();
            var isPdf = file.ContentType == "application/pdf"
                || file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

            if (isPdf)
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                ms.Position = 0;

                using var pdfDoc = PdfDocument.Open(ms.ToArray());
                foreach (var page in pdfDoc.GetPages())
                    text.AppendLine(page.Text);
            }
            else
            {
                // For images we cannot do OCR without Tesseract — return no-date signal
                return Ok(new { found = false, date = (string?)null, message = "Image files cannot be auto-scanned. Please enter the date manually." });
            }

            var raw = text.ToString();
            var detected = ExtractExpiryDate(raw);

            if (detected != null)
                return Ok(new { found = true, date = detected.Value.ToString("yyyy-MM-dd"), message = $"Expiry date detected: {detected.Value:dd-MMM-yyyy}" });

            return Ok(new { found = false, date = (string?)null, message = "No expiry date found automatically. Please enter it manually." });
        }
        catch (Exception ex)
        {
            return Ok(new { found = false, date = (string?)null, message = $"Could not parse document: {ex.Message}" });
        }
    }

    /// <summary>
    /// Extracts the most likely expiry/validity date from free text.
    /// Priority: matches with expiry keywords in a surrounding 150-character window.
    /// </summary>
    public static DateTime? ExtractExpiryDate(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        // Date patterns to try (ordered by specificity, year can be 2-4 digits)
        var datePatterns = new[]
        {
            @"\b(\d{1,2})[\/\-\.](\d{1,2})[\/\-\.](\d{2,4})\b",      // dd/mm/yyyy  or dd/mm/yy
            @"\b(\d{1,2})\s+(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*[\s,]+(\d{2,4})\b", // dd Mon YYYY/YY
            @"\b(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*[\s,]+(\d{1,2})[\s,]+(\d{2,4})\b", // Mon dd, YYYY/YY
            @"\b(\d{4})[\/\-\.](\d{1,2})[\/\-\.](\d{1,2})\b"        // yyyy-mm-dd
        };

        // Keywords that signal an expiry date is nearby
        var expiryKeywords = new[]
        {
            "expir", "valid till", "valid upto", "validity", "renewal date",
            "renew", "due date", "date of expiry", "expire", "validity upto", "validity date"
        };

        var matches = new List<(DateTime Date, int Index, bool HasKeywordNearby)>();

        foreach (var pattern in datePatterns)
        {
            var rx = new Regex(pattern, RegexOptions.IgnoreCase);
            var mc = rx.Matches(text);
            foreach (Match m in mc)
            {
                if (TryParseDate(m, pattern, out var dt))
                {
                    // Look for expiry keywords in a surrounding window
                    int startIdx = Math.Max(0, m.Index - 150);
                    int endIdx = Math.Min(text.Length, m.Index + m.Length + 150);
                    var surroundingText = text.Substring(startIdx, endIdx - startIdx).ToLower();
                    
                    bool keywordNearby = expiryKeywords.Any(k => surroundingText.Contains(k));
                    matches.Add((dt, m.Index, keywordNearby));
                }
            }
        }

        if (matches.Count == 0) return null;

        // Group 1: Has keyword nearby and is in the future.
        var futureWithKeyword = matches
            .Where(m => m.HasKeywordNearby && m.Date > DateTime.Today)
            .OrderByDescending(m => m.Date)
            .ToList();
        if (futureWithKeyword.Any()) return futureWithKeyword.First().Date;

        // Group 2: Has keyword nearby (even if past/today).
        var allWithKeyword = matches
            .Where(m => m.HasKeywordNearby)
            .OrderByDescending(m => m.Date)
            .ToList();
        if (allWithKeyword.Any()) return allWithKeyword.First().Date;

        // Group 3: No keyword, but is in the future.
        var futureNoKeyword = matches
            .Where(m => m.Date > DateTime.Today)
            .OrderByDescending(m => m.Date)
            .ToList();
        if (futureNoKeyword.Any()) return futureNoKeyword.First().Date;

        // Group 4: Fallback to the latest date found in the document overall.
        return matches.OrderByDescending(m => m.Date).First().Date;
    }

    private static bool TryParseDate(Match m, string pattern, out DateTime result)
    {
        result = default;
        try
        {
            // Pattern: dd/mm/yyyy
            if (pattern.StartsWith(@"\b(\d{1,2})[\/"))
            {
                int p1 = int.Parse(m.Groups[1].Value);
                int p2 = int.Parse(m.Groups[2].Value);
                int yr = int.Parse(m.Groups[3].Value);
                if (yr < 100) yr += 2000; // handle 2-digit year!
                // Try dd/mm/yyyy first (Indian standard)
                if (p2 >= 1 && p2 <= 12 && p1 >= 1 && p1 <= 31)
                    if (DateTime.TryParse($"{yr}-{p2:D2}-{p1:D2}", out result)) return true;
                // Fallback mm/dd/yyyy
                if (p1 >= 1 && p1 <= 12 && p2 >= 1 && p2 <= 31)
                    if (DateTime.TryParse($"{yr}-{p1:D2}-{p2:D2}", out result)) return true;
                return false;
            }
            // Pattern: dd Mon YYYY
            if (pattern.Contains(@"Jan|Feb"))
            {
                var combined = m.Value;
                if (DateTime.TryParse(combined, out result)) return true;
                return false;
            }
            // Pattern: yyyy-mm-dd
            if (pattern.StartsWith(@"\b(\d{4})"))
            {
                int yr = int.Parse(m.Groups[1].Value);
                int mo = int.Parse(m.Groups[2].Value);
                int dy = int.Parse(m.Groups[3].Value);
                if (DateTime.TryParse($"{yr}-{mo:D2}-{dy:D2}", out result)) return true;
                return false;
            }
        }
        catch { }
        return false;
    }
}
