using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using IoclFleetApi.Data;
using IoclFleetApi.DTOs;
using IoclFleetApi.Services;

namespace IoclFleetApi.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly IComplianceService _compliance;

    // IOCL brand colours
    private const string Blue   = "#0054A6";
    private const string Orange = "#FF6B00";
    private const string Dark   = "#1E293B";

    private static readonly Dictionary<string, string> LicenseNames = new()
    {
        ["ROAD_PERMIT"]       = "Road Permit (By RTO)",
        ["AGE_DETERMINATION"] = "Date of Manufacture / Age Determination",
        ["PUC"]               = "Pollution Under Control (PUC)",
        ["FITNESS"]           = "Fitness License (By RTO)",
        ["EXPLOSIVE"]         = "Explosive License",
        ["GREEN_CARD"]        = "Green Card",
        ["INSURANCE"]         = "Vehicle Insurance",
        ["CALIBRATION"]       = "Calibration Certificate"
    };

    public ReportsController(AppDbContext db, IAuditService audit, IComplianceService compliance)
    {
        _db = db; _audit = audit; _compliance = compliance;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>Loads the bundled IOCL logo. Returns null if not found (graceful fallback).</summary>
    private static byte[]? GetLogoBytes()
    {
        // Resolve relative to the executing assembly location (works in both dev & published)
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "assets", "iocl-logo.jpg"),
            Path.Combine(baseDir, "..", "assets", "iocl-logo.jpg"),
            Path.Combine(Directory.GetCurrentDirectory(), "assets", "iocl-logo.jpg")
        };
        foreach (var p in candidates)
        {
            if (System.IO.File.Exists(p)) return System.IO.File.ReadAllBytes(p);
        }
        return null;
    }

    private (int id, string username, string role, int? departmentId) GetCurrentUser()
    {
        var id       = int.Parse(User.FindFirst("id")!.Value);
        var username = User.FindFirst("username")!.Value;
        var role     = User.FindFirst(System.Security.Claims.ClaimTypes.Role)!.Value;
        var user     = _db.Users.Find(id);
        return (id, username, role, user?.DepartmentId);
    }

    private static int? GetDaysRemaining(string? expiryDate)
    {
        if (string.IsNullOrEmpty(expiryDate) || !DateTime.TryParse(expiryDate, out var expiry)) return null;
        return (int)Math.Ceiling((expiry.Date - DateTime.Today).TotalDays);
    }

    private static string FmtDate(string? d)
    {
        if (string.IsNullOrEmpty(d)) return "PENDING";
        return DateTime.TryParse(d, out var dt) ? dt.ToString("dd-MMM-yyyy") : d;
    }

    // ── ROUTES ────────────────────────────────────────────────────────

    [HttpGet("compliance")]
    public async Task<IActionResult> GetComplianceReport([FromQuery] string? format, [FromQuery] int? departmentId)
    {
        var (userId, username, role, userDeptId) = GetCurrentUser();
        var isDeptAdmin  = role == "DEPT_ADMIN";
        var activeDeptId = isDeptAdmin ? userDeptId : departmentId;

        var query = _db.Vehicles
            .Include(v => v.Department)
            .Include(v => v.ComplianceRecords)
            .OrderBy(v => v.VehicleNumber)
            .AsQueryable();

        if (activeDeptId.HasValue) query = query.Where(v => v.DepartmentId == activeDeptId.Value);
        var vehicles = await query.ToListAsync();

        var deptFilterName = activeDeptId.HasValue && vehicles.Count > 0 && vehicles[0].Department != null
            ? vehicles[0].Department!.Name : "All Departments";

        await _audit.LogAction(userId, username, "GENERATE_REPORT",
            $"Generated {(format ?? "PDF").ToUpper()} Compliance Report for {deptFilterName}.",
            departmentId: isDeptAdmin ? userDeptId : null,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        if (format == "excel") return GenerateComplianceExcel(vehicles, deptFilterName);
        return GenerateCompliancePdf(vehicles, deptFilterName, username, role);
    }

    [HttpGet("expiries")]
    public async Task<IActionResult> GetExpiryReport([FromQuery] string? format)
    {
        var (userId, username, role, userDeptId) = GetCurrentUser();
        var isDeptAdmin = role == "DEPT_ADMIN";

        var query = _db.ComplianceRecords
            .Include(c => c.Vehicle!).ThenInclude(v => v.Department)
            .Where(c => c.Status == "EXPIRED" || c.Status == "HIGH_CRITICAL"
                     || c.Status == "MEDIUM_CRITICAL" || c.Status == "WARNING")
            .AsQueryable();

        if (isDeptAdmin) query = query.Where(c => c.Vehicle!.DepartmentId == userDeptId);
        var records = await query.OrderBy(c => c.Status).ThenBy(c => c.ExpiryDate).ToListAsync();

        await _audit.LogAction(userId, username, "GENERATE_REPORT",
            $"Generated Expiry Alerts Report. Found {records.Count} documents requiring renewal.",
            departmentId: isDeptAdmin ? userDeptId : null,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        if (format == "excel") return GenerateExpiryExcel(records);
        return GenerateExpiryPdf(records, username, role);
    }

    private IActionResult GenerateComplianceExcel(List<Models.Vehicle> vehicles, string deptFilterName)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet("Fleet Compliance");

        // ── Logo ──────────────────────────────────────────────────────
        var logoBytes = GetLogoBytes();
        int dataStartRow = 1;
        if (logoBytes != null)
        {
            using var logoStream = new MemoryStream(logoBytes);
            var pic = ws.AddPicture(logoStream)
                        .MoveTo(ws.Cell("A1"))
                        .WithSize(120, 55);
            // Push data rows down below the logo
            dataStartRow = 4;
            ws.Row(1).Height = 42;
            ws.Row(2).Height = 8;
            ws.Row(3).Height = 20;
        }

        // ── Title rows ────────────────────────────────────────────────
        int titleRow  = dataStartRow;
        int subRow    = dataStartRow + 1;
        int headerRow = dataStartRow + 2;

        ws.Range(ws.Cell(titleRow, 1), ws.Cell(titleRow, 9)).Merge()
            .SetValue("INDIAN OIL CORPORATION LIMITED (IOCL) — Panipat Refinery");
        ws.Cell(titleRow, 1).Style.Font.SetBold(true).Font.SetFontSize(15).Font.SetFontColor(XLColor.White);
        ws.Cell(titleRow, 1).Style.Fill.SetBackgroundColor(XLColor.FromHtml(Blue));
        ws.Cell(titleRow, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        ws.Range(ws.Cell(subRow, 1), ws.Cell(subRow, 9)).Merge()
            .SetValue($"Fleet Compliance Status Report — {deptFilterName}  |  Generated: {DateTime.Now:dd/MM/yyyy HH:mm}");
        ws.Cell(subRow, 1).Style.Font.SetItalic(true).Font.SetFontSize(10);
        ws.Cell(subRow, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        var headers = new[] { "Veh. No", "Type", "Department", "Driver", "Vendor", "Overall Status", "Compliant Docs", "Expiring Docs", "Expired Docs" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(headerRow, i + 1);
            cell.SetValue(headers[i]);
            cell.Style.Font.SetBold(true).Font.SetFontColor(XLColor.White);
            cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml(Orange));
            cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        }

        int row = headerRow + 1;
        foreach (var v in vehicles)
        {
            var recs = v.ComplianceRecords.ToList();
            ws.Cell(row, 1).SetValue(v.VehicleNumber);
            ws.Cell(row, 2).SetValue(v.VehicleType);
            ws.Cell(row, 3).SetValue(v.Department?.Name ?? "N/A");
            ws.Cell(row, 4).SetValue(v.DriverName ?? "N/A");
            ws.Cell(row, 5).SetValue(v.VendorName ?? "N/A");
            ws.Cell(row, 6).SetValue(v.OverallStatus);
            ws.Cell(row, 7).SetValue(recs.Count(r => r.Status == "ACTIVE"));
            ws.Cell(row, 8).SetValue(recs.Count(r => r.Status is "WARNING" or "MEDIUM_CRITICAL" or "HIGH_CRITICAL"));
            ws.Cell(row, 9).SetValue(recs.Count(r => r.Status == "EXPIRED"));

            var statusColor = v.OverallStatus switch
            {
                "FULLY_COMPLIANT" => XLColor.Green,
                "WARNING"         => XLColor.FromHtml("#D4AF37"),
                _                 => XLColor.Red
            };
            ws.Cell(row, 6).Style.Font.SetBold(true).Font.SetFontColor(statusColor);
            row++;
        }

        ws.Columns().AdjustToContents();
        ws.Column(1).Width = Math.Max(ws.Column(1).Width, 16);
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"IOCL_Compliance_Report_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.xlsx");
    }

    private IActionResult GenerateExpiryExcel(List<Models.ComplianceRecord> records)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet("Documents Needing Renewal");

        // ── Logo ──────────────────────────────────────────────────────
        var logoBytes = GetLogoBytes();
        int dataStartRow = 1;
        if (logoBytes != null)
        {
            using var logoStream = new MemoryStream(logoBytes);
            ws.AddPicture(logoStream)
              .MoveTo(ws.Cell("A1"))
              .WithSize(120, 55);
            dataStartRow = 4;
            ws.Row(1).Height = 42;
            ws.Row(2).Height = 8;
            ws.Row(3).Height = 20;
        }

        int titleRow  = dataStartRow;
        int subRow    = dataStartRow + 1;
        int headerRow = dataStartRow + 2;

        ws.Range(ws.Cell(titleRow, 1), ws.Cell(titleRow, 8)).Merge()
            .SetValue("IOCL Panipat Refinery — Documents Requiring Renewal");
        ws.Cell(titleRow, 1).Style.Font.SetBold(true).Font.SetFontSize(13).Font.SetFontColor(XLColor.White);
        ws.Cell(titleRow, 1).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#DC2626"));
        ws.Cell(titleRow, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        ws.Range(ws.Cell(subRow, 1), ws.Cell(subRow, 8)).Merge()
            .SetValue($"Generated: {DateTime.Now:dd/MM/yyyy HH:mm}  |  Total Flagged: {records.Count}");
        ws.Cell(subRow, 1).Style.Font.SetItalic(true).Font.SetFontSize(10);
        ws.Cell(subRow, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        var headers = new[] { "Vehicle No", "Department", "Document / License Type", "License Number", "Issuing Authority", "Expiry Date", "Alert Status", "Days Remaining" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(headerRow, i + 1);
            cell.SetValue(headers[i]);
            cell.Style.Font.SetBold(true).Font.SetFontColor(XLColor.White);
            cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml(Orange));
        }

        var statusOrder = new Dictionary<string, int> { ["EXPIRED"] = 0, ["HIGH_CRITICAL"] = 1, ["MEDIUM_CRITICAL"] = 2, ["WARNING"] = 3 };
        var sorted = records.OrderBy(r => statusOrder.GetValueOrDefault(r.Status, 4)).ToList();

        int row = headerRow + 1;
        foreach (var r in sorted)
        {
            var days      = GetDaysRemaining(r.ExpiryDate);
            var daysLabel = days == null ? "N/A" : days < 0 ? $"{Math.Abs(days.Value)} days overdue" : $"{days.Value} days remaining";

            ws.Cell(row, 1).SetValue(r.Vehicle?.VehicleNumber ?? "N/A");
            ws.Cell(row, 2).SetValue(r.Vehicle?.Department?.Name ?? "N/A");
            ws.Cell(row, 3).SetValue(LicenseNames.GetValueOrDefault(r.LicenseType, r.LicenseType.Replace("_", " ")));
            ws.Cell(row, 4).SetValue(r.LicenseNumber ?? "PENDING");
            ws.Cell(row, 5).SetValue(r.IssuingAuthority ?? "N/A");
            ws.Cell(row, 6).SetValue(FmtDate(r.ExpiryDate));
            ws.Cell(row, 7).SetValue(r.Status.Replace("_", " "));
            ws.Cell(row, 8).SetValue(daysLabel);

            var statusColor = r.Status switch
            {
                "EXPIRED"         => XLColor.FromHtml("#CC0000"),
                "HIGH_CRITICAL"   => XLColor.FromHtml("#DC4E00"),
                "MEDIUM_CRITICAL" => XLColor.FromHtml("#EA580C"),
                _                 => XLColor.FromHtml("#D97706")
            };
            ws.Cell(row, 7).Style.Font.SetBold(true).Font.SetFontColor(statusColor);
            row++;
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"IOCL_Expiry_Report_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.xlsx");
    }

    // ── PDF GENERATORS (QuestPDF) ─────────────────────────────────────

    private IActionResult GenerateCompliancePdf(
        List<Models.Vehicle> vehicles, string deptFilterName, string username, string role)
    {
        var compliant = vehicles.Count(v => v.OverallStatus == "FULLY_COMPLIANT");
        var warning   = vehicles.Count(v => v.OverallStatus == "WARNING");
        var expired   = vehicles.Count(v => v.OverallStatus is "EXPIRED" or "CRITICAL" or "HIGH_CRITICAL");

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.2f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                page.Header().Column(col =>
                {
                    col.Item().Row(hrow =>
                    {
                        // Logo panel
                        var logoBytes = GetLogoBytes();
                        if (logoBytes != null)
                        {
                            hrow.ConstantItem(90).Background("#FFFFFF").Padding(6)
                                .Image(logoBytes).FitArea();
                        }

                        // Title panel
                        hrow.RelativeItem().Background(Blue).Padding(12).Column(c =>
                        {
                            c.Item().Text("INDIAN OIL CORPORATION LIMITED")
                                .FontSize(16).Bold().FontColor(Colors.White);
                            c.Item().Text("Panipat Refinery — Fleet Compliance & Safety Management Portal")
                                .FontSize(9).FontColor("#93C5FD");
                        });
                    });
                    col.Item().Height(4).Background(Orange);
                    col.Item().PaddingVertical(6).Row(row =>
                    {
                        row.RelativeItem()
                            .Text($"Fleet Compliance Audit Report  |  Department: {deptFilterName}")
                            .FontSize(10).Bold().FontColor(Dark);
                        row.ConstantItem(260).AlignRight()
                            .Text($"Generated: {DateTime.Now:dd/MM/yyyy HH:mm}  |  By: {username} ({role})")
                            .FontSize(8).FontColor("#64748B");
                    });
                });

                page.Content().Column(col =>
                {
                    // Summary boxes
                    col.Item().PaddingBottom(8).Row(row =>
                    {
                        void Stat(string label, string val, string bg, string fg)
                        {
                            row.RelativeItem().Background(bg).Padding(8).Column(c =>
                            {
                                c.Item().Text(val).FontSize(20).Bold().FontColor(fg);
                                c.Item().Text(label).FontSize(8).FontColor(fg);
                            });
                        }
                        Stat("Total Vehicles",     vehicles.Count.ToString(), "#EFF6FF", Blue);
                        row.ConstantItem(6);
                        Stat("Fully Compliant",    compliant.ToString(),      "#DCFCE7", "#16A34A");
                        row.ConstantItem(6);
                        Stat("Warning",            warning.ToString(),        "#FEF9C3", "#D97706");
                        row.ConstantItem(6);
                        Stat("Expired / Critical", expired.ToString(),        "#FEE2E2", "#DC2626");
                    });

                    // Table
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(2.0f);
                            cols.RelativeColumn(2.0f);
                            cols.RelativeColumn(2.5f);
                            cols.RelativeColumn(2.5f);
                            cols.RelativeColumn(1.5f);
                            cols.RelativeColumn(1.5f);
                            cols.RelativeColumn(1.5f);
                            cols.RelativeColumn(2.5f);
                        });

                        string[] hdrs = { "Vehicle No", "Type", "Department", "Driver",
                                          "Compliant", "Expiring", "Expired", "Overall Status" };
                        table.Header(header =>
                        {
                            foreach (var h in hdrs)
                                header.Cell().Background(Blue).Padding(6)
                                    .Text(h).FontSize(8).Bold().FontColor("#FFFFFF");
                        });

                        bool alt = false;
                        foreach (var v in vehicles)
                        {
                            var recs = v.ComplianceRecords.ToList();
                            var bg   = alt ? "#F1F5F9" : "#FFFFFF";
                            var (statusBg, statusFg) = v.OverallStatus switch
                            {
                                "FULLY_COMPLIANT" => ("#DCFCE7", "#16A34A"),
                                "WARNING"         => ("#FEF9C3", "#D97706"),
                                _                 => ("#FEE2E2", "#DC2626")
                            };

                            void C(string txt, string? fg2 = null)
                            {
                                var t = table.Cell().Background(bg).Padding(5).Text(txt).FontSize(8);
                                if (fg2 != null) t.FontColor(fg2);
                            }

                            C(v.VehicleNumber);
                            C(v.VehicleType);
                            C(v.Department?.Name ?? "N/A");
                            C(v.DriverName ?? "N/A");
                            C(recs.Count(r => r.Status == "ACTIVE").ToString());
                            C(recs.Count(r => r.Status is "WARNING" or "MEDIUM_CRITICAL" or "HIGH_CRITICAL").ToString(), "#D97706");
                            C(recs.Count(r => r.Status == "EXPIRED").ToString(), "#DC2626");
                            table.Cell().Background(statusBg).Padding(5)
                                .Text(v.OverallStatus.Replace("_", " "))
                                .FontSize(8).Bold().FontColor(statusFg);

                            alt = !alt;
                        }
                    });
                });

                page.Footer().BorderTop(1).BorderColor("#E2E8F0").PaddingTop(4).Row(row =>
                {
                    row.RelativeItem()
                        .Text("CONFIDENTIAL — FOR INTERNAL REFINERY USE ONLY | IOCL Panipat Refinery")
                        .FontSize(7).FontColor("#94A3B8");
                    row.ConstantItem(100).AlignRight().Text(txt =>
                    {
                        txt.Span("Page ").FontSize(7).FontColor("#94A3B8");
                        txt.CurrentPageNumber().FontSize(7).FontColor("#94A3B8");
                        txt.Span(" of ").FontSize(7).FontColor("#94A3B8");
                        txt.TotalPages().FontSize(7).FontColor("#94A3B8");
                    });
                });
            });
        });

        var bytes = doc.GeneratePdf();
        return File(bytes, "application/pdf",
            $"IOCL_Compliance_Report_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.pdf");
    }

    private IActionResult GenerateExpiryPdf(List<Models.ComplianceRecord> records, string username, string role)
    {
        var statusOrder = new Dictionary<string, int>
        {
            ["EXPIRED"] = 0, ["HIGH_CRITICAL"] = 1, ["MEDIUM_CRITICAL"] = 2, ["WARNING"] = 3
        };
        var sorted = records.OrderBy(r => statusOrder.GetValueOrDefault(r.Status, 4)).ToList();

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.2f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                page.Header().Column(col =>
                {
                    col.Item().Row(hrow =>
                    {
                        // Logo panel
                        var logoBytes = GetLogoBytes();
                        if (logoBytes != null)
                        {
                            hrow.ConstantItem(90).Background("#FFFFFF").Padding(6)
                                .Image(logoBytes).FitArea();
                        }

                        // Title panel
                        hrow.RelativeItem().Background("#7F1D1D").Padding(12).Column(c =>
                        {
                            c.Item().Text("INDIAN OIL CORPORATION LIMITED")
                                .FontSize(16).Bold().FontColor(Colors.White);
                            c.Item().Text("Panipat Refinery — Compliance Expiry & Renewal Alert Register")
                                .FontSize(9).FontColor("#FCA5A5");
                        });
                    });
                    col.Item().Height(4).Background(Orange);
                    col.Item().PaddingVertical(6).Row(row =>
                    {
                        row.RelativeItem()
                            .Text($"Documents Requiring Renewal  |  Total Flagged: {sorted.Count}")
                            .FontSize(10).Bold().FontColor(Dark);
                        row.ConstantItem(260).AlignRight()
                            .Text($"Generated: {DateTime.Now:dd/MM/yyyy HH:mm}  |  By: {username} ({role})")
                            .FontSize(8).FontColor("#64748B");
                    });
                });

                page.Content().Column(col =>
                {
                    // Summary boxes
                    col.Item().PaddingBottom(8).Row(row =>
                    {
                        void Stat(string label, string val, string bg, string fg)
                        {
                            row.RelativeItem().Background(bg).Padding(8).Column(c =>
                            {
                                c.Item().Text(val).FontSize(20).Bold().FontColor(fg);
                                c.Item().Text(label).FontSize(8).FontColor(fg);
                            });
                        }
                        Stat("Total Flagged",   sorted.Count.ToString(),                                            "#FFF7ED", "#EA580C");
                        row.ConstantItem(6);
                        Stat("Expired",         sorted.Count(r => r.Status == "EXPIRED").ToString(),         "#FEE2E2", "#DC2626");
                        row.ConstantItem(6);
                        Stat("High Critical",   sorted.Count(r => r.Status == "HIGH_CRITICAL").ToString(),   "#FFF7ED", "#EA580C");
                        row.ConstantItem(6);
                        Stat("Medium Critical", sorted.Count(r => r.Status == "MEDIUM_CRITICAL").ToString(), "#FEF9C3", "#D97706");
                        row.ConstantItem(6);
                        Stat("Warning",         sorted.Count(r => r.Status == "WARNING").ToString(),         "#FFFBEB", "#F59E0B");
                    });

                    // Table
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(2.0f);
                            cols.RelativeColumn(2.5f);
                            cols.RelativeColumn(3.5f);
                            cols.RelativeColumn(2.0f);
                            cols.RelativeColumn(2.5f);
                            cols.RelativeColumn(1.8f);
                            cols.RelativeColumn(2.0f);
                            cols.RelativeColumn(2.0f);
                        });

                        string[] hdrs = { "Vehicle No", "Department", "Document / License Type",
                                          "License No", "Issuing Authority", "Expiry Date",
                                          "Alert Status", "Days Remaining" };
                        table.Header(header =>
                        {
                            foreach (var h in hdrs)
                                header.Cell().Background("#7F1D1D").Padding(6)
                                    .Text(h).FontSize(8).Bold().FontColor("#FFFFFF");
                        });

                        bool alt = false;
                        foreach (var r in sorted)
                        {
                            var bg   = alt ? "#F1F5F9" : "#FFFFFF";
                            var days = GetDaysRemaining(r.ExpiryDate);
                            var dayTx = days == null ? "N/A"
                                      : days < 0 ? $"{Math.Abs(days.Value)}d overdue"
                                      : $"{days.Value}d remaining";
                            var fg = r.Status switch
                            {
                                "EXPIRED"         => "#DC2626",
                                "HIGH_CRITICAL"   => "#EA580C",
                                "MEDIUM_CRITICAL" => "#D97706",
                                _                 => "#F59E0B"
                            };

                            void C(string txt, string? color = null)
                            {
                                var t = table.Cell().Background(bg).Padding(5).Text(txt).FontSize(8);
                                if (color != null) t.Bold().FontColor(color);
                            }

                            C(r.Vehicle?.VehicleNumber ?? "N/A");
                            C(r.Vehicle?.Department?.Name ?? "N/A");
                            C(LicenseNames.GetValueOrDefault(r.LicenseType, r.LicenseType.Replace("_", " ")));
                            C(r.LicenseNumber ?? "PENDING");
                            C(r.IssuingAuthority ?? "N/A");
                            C(FmtDate(r.ExpiryDate), fg);
                            C(r.Status.Replace("_", " "), fg);
                            C(dayTx, fg);

                            alt = !alt;
                        }
                    });
                });

                page.Footer().BorderTop(1).BorderColor("#E2E8F0").PaddingTop(4).Row(row =>
                {
                    row.RelativeItem()
                        .Text("CONFIDENTIAL — FOR INTERNAL REFINERY USE ONLY | IOCL Panipat Refinery")
                        .FontSize(7).FontColor("#94A3B8");
                    row.ConstantItem(100).AlignRight().Text(txt =>
                    {
                        txt.Span("Page ").FontSize(7).FontColor("#94A3B8");
                        txt.CurrentPageNumber().FontSize(7).FontColor("#94A3B8");
                        txt.Span(" of ").FontSize(7).FontColor("#94A3B8");
                        txt.TotalPages().FontSize(7).FontColor("#94A3B8");
                    });
                });
            });
        });

        var bytes = doc.GeneratePdf();
        return File(bytes, "application/pdf",
            $"IOCL_Expiry_Report_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.pdf");
    }
}
