using MailKit.Net.Smtp;
using MimeKit;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using IoclFleetApi.Models;

namespace IoclFleetApi.Services;

public interface IEmailService
{
    Task SendComplianceAlert(string toEmail, string toName, string vehicleNumber, string vehicleType,
        string department, string licenseType, string expiryDate, int daysRemaining, string status);
    Task SendDailySummary(string toEmail, string toName, int totalVehicles, int expiredCount,
        int criticalCount, int warningCount, List<DepartmentBreakdown> departmentBreakdown,
        List<ComplianceRecord>? expiringRecords = null);
}

public class DepartmentBreakdown
{
    public string Name { get; set; } = string.Empty;
    public int VehicleCount { get; set; }
    public double ComplianceScore { get; set; }
}

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    private async Task SendEmail(string to, string subject, string htmlBody, byte[]? pdfAttachment = null, string? attachmentName = null)
    {
        var host = _config["Email:Host"] ?? "smtp.ethereal.email";
        var port = int.Parse(_config["Email:Port"] ?? "587");
        var user = _config["Email:User"] ?? "";
        var pass = _config["Email:Pass"] ?? "";
        var fromAddress = _config["Email:FromAddress"] ?? "compliance-noreply@iocl.co.in";
        var fromName = _config["Email:FromName"] ?? "IOCL Fleet Compliance System";

        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
        {
            _logger.LogWarning("[EmailService] No SMTP credentials configured. Skipping email.");
            return;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, fromAddress));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;

            var builder = new BodyBuilder();
            builder.HtmlBody = htmlBody;
            if (pdfAttachment != null && !string.IsNullOrEmpty(attachmentName))
            {
                builder.Attachments.Add(attachmentName, pdfAttachment, new MimeKit.ContentType("application", "pdf"));
            }
            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            client.Timeout = 8000; // Fail fast (8 seconds) if SMTP is blocked in hosting environment
            await client.ConnectAsync(host, port, MailKit.Security.SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(user, pass);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("[EmailService] Email sent to {To}", to);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EmailService] Failed to send email to {To}", to);
        }
    }

    public async Task SendComplianceAlert(string toEmail, string toName, string vehicleNumber,
        string vehicleType, string department, string licenseType, string expiryDate,
        int daysRemaining, string status)
    {
        var isExpired = daysRemaining <= 0;
        var urgency = isExpired ? "IMMEDIATE ACTION REQUIRED"
            : daysRemaining <= 7 ? "URGENT: Expiring Soon"
            : "Compliance Reminder";

        var subject = isExpired
            ? $"EXPIRED: {vehicleNumber} — {licenseType.Replace("_", " ")} | IOCL Refinery"
            : $"{daysRemaining}d Left: {vehicleNumber} — {licenseType.Replace("_", " ")} | IOCL Refinery";

        var html = $@"
<div style=""font-family:Arial,sans-serif;padding:20px;border:1px solid #e2e8f0;border-radius:8px;max-width:600px;"">
  <h2 style=""color:#0054A6;border-bottom:2px solid #FF6B00;padding-bottom:10px;"">IOCL Panipat Refinery Fleet Compliance Alert</h2>
  <p>Dear {toName},</p>
  <p><strong>{urgency}</strong></p>
  <table style=""width:100%;border-collapse:collapse;margin-top:15px;"">
    <tr style=""background-color:#f8fafc;""><td style=""padding:8px;font-weight:bold;border:1px solid #cbd5e1;"">Vehicle Number:</td><td style=""padding:8px;border:1px solid #cbd5e1;"">{vehicleNumber}</td></tr>
    <tr><td style=""padding:8px;font-weight:bold;border:1px solid #cbd5e1;"">Vehicle Type:</td><td style=""padding:8px;border:1px solid #cbd5e1;"">{vehicleType}</td></tr>
    <tr style=""background-color:#f8fafc;""><td style=""padding:8px;font-weight:bold;border:1px solid #cbd5e1;"">Department:</td><td style=""padding:8px;border:1px solid #cbd5e1;"">{department}</td></tr>
    <tr><td style=""padding:8px;font-weight:bold;border:1px solid #cbd5e1;"">Certificate Type:</td><td style=""padding:8px;border:1px solid #cbd5e1;"">{licenseType.Replace("_", " ")}</td></tr>
    <tr style=""background-color:#f8fafc;""><td style=""padding:8px;font-weight:bold;border:1px solid #cbd5e1;"">Expiry Date:</td><td style=""padding:8px;font-weight:bold;color:#dc2626;border:1px solid #cbd5e1;"">{expiryDate}</td></tr>
    <tr><td style=""padding:8px;font-weight:bold;border:1px solid #cbd5e1;"">Status:</td><td style=""padding:8px;font-weight:bold;color:#dc2626;border:1px solid #cbd5e1;"">{status}</td></tr>
    <tr style=""background-color:#f8fafc;""><td style=""padding:8px;font-weight:bold;border:1px solid #cbd5e1;"">Days Remaining:</td><td style=""padding:8px;border:1px solid #cbd5e1;"">{daysRemaining}</td></tr>
  </table>
  <p style=""margin-top:20px;"">Please log in to the refinery compliance dashboard to renew the certificate.</p>
  <hr style=""border:none;border-top:1px solid #cbd5e1;margin:20px 0;"" />
  <p style=""font-size:11px;color:#64748b;"">This is a system generated email. Do not reply. IOCL Fleet Compliance Dept.</p>
</div>";

        await SendEmail(toEmail, subject, html);
    }

    public async Task SendDailySummary(string toEmail, string toName, int totalVehicles,
        int expiredCount, int criticalCount, int warningCount,
        List<DepartmentBreakdown> departmentBreakdown,
        List<ComplianceRecord>? expiringRecords = null)
    {
        var subject = $"Daily Compliance Digest — {DateTime.Now:dd/MM/yyyy} | IOCL Panipat Refinery";
        var deptRows = string.Join("", departmentBreakdown.Select((d, i) =>
            $"<tr style=\"{(i % 2 == 0 ? "background:#fff;" : "background:#f8fafc;")}\"><td style=\"padding:8px 12px;\">{d.Name}</td><td style=\"text-align:center;padding:8px;\">{d.VehicleCount}</td><td style=\"text-align:center;padding:8px;font-weight:bold;color:{(d.ComplianceScore >= 80 ? "#16a34a" : d.ComplianceScore >= 60 ? "#d97706" : "#dc2626")};\">{d.ComplianceScore}%</td></tr>"));

        var html = $@"
<div style=""font-family:Arial,sans-serif;padding:20px;max-width:600px;"">
  <h2 style=""color:#0054A6;"">IOCL Daily Compliance Digest</h2>
  <p>Good morning, {toName}.</p>
  <p>Fleet: <strong>{totalVehicles}</strong> | Expired: <strong style=""color:#dc2626"">{expiredCount}</strong> | Critical: <strong style=""color:#ea580c"">{criticalCount}</strong> | Warning: <strong style=""color:#d97706"">{warningCount}</strong></p>
  <p>Please find attached the PDF report listing only the vehicles and documents that are going to expire or have expired.</p>
  <table style=""width:100%;border-collapse:collapse;font-size:12px;border:1px solid #e2e8f0;"">
    <tr style=""background:#f8fafc;""><th style=""padding:8px;text-align:left;"">Department</th><th style=""padding:8px;text-align:center;"">Vehicles</th><th style=""padding:8px;text-align:center;"">Score</th></tr>
    {deptRows}
  </table>
  <hr /><p style=""font-size:11px;color:#64748b;"">System generated. Do not reply.</p>
</div>";

        byte[]? pdfAttachment = null;
        if (expiringRecords != null && expiringRecords.Count > 0)
        {
            try
            {
                pdfAttachment = GenerateExpiryPdfBytes(expiringRecords);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EmailService] Failed to generate PDF attachment for daily digest.");
            }
        }

        await SendEmail(toEmail, subject, html, pdfAttachment, "Expiring_Vehicles_Report.pdf");
    }

    private byte[] GenerateExpiryPdfBytes(List<ComplianceRecord> records)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        
        var statusOrder = new Dictionary<string, int>
        {
            ["EXPIRED"] = 0, ["HIGH_CRITICAL"] = 1, ["MEDIUM_CRITICAL"] = 2, ["WARNING"] = 3
        };
        var sorted = records.OrderBy(r => statusOrder.GetValueOrDefault(r.Status, 4)).ToList();

        var doc = QuestPDF.Fluent.Document.Create(container =>
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
                        hrow.RelativeItem().Background("#7F1D1D").Padding(12).Column(c =>
                        {
                            c.Item().Text("INDIAN OIL CORPORATION LIMITED")
                                .FontSize(16).Bold().FontColor(Colors.White);
                            c.Item().Text("Panipat Refinery — Compliance Expiry & Renewal Alert Register")
                                .FontSize(9).FontColor("#FCA5A5");
                        });
                    });
                    col.Item().Height(4).Background("#FF6B00");
                    col.Item().PaddingVertical(6).Row(row =>
                    {
                        row.RelativeItem()
                            .Text($"Documents Requiring Renewal  |  Total Flagged: {sorted.Count}")
                            .FontSize(10).Bold().FontColor("#1E293B");
                        row.ConstantItem(260).AlignRight()
                            .Text($"Generated: {DateTime.Now:dd/MM/yyyy HH:mm}")
                            .FontSize(8).FontColor("#64748B");
                    });
                });

                page.Content().Column(col =>
                {
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
                            var bg = alt ? "#F1F5F9" : "#FFFFFF";
                            
                            int? days = null;
                            if (DateTime.TryParse(r.ExpiryDate, out var expiry))
                            {
                                days = (int)Math.Ceiling((expiry.Date - DateTime.Today).TotalDays);
                            }
                            
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
                            
                            var licenseName = r.LicenseType.Replace("_", " ");
                            if (r.LicenseType == "ROAD_PERMIT") licenseName = "Road Permit (By RTO)";
                            else if (r.LicenseType == "AGE_DETERMINATION") licenseName = "Date of Manufacture / Age Determination";
                            else if (r.LicenseType == "PUC") licenseName = "Pollution Under Control (PUC)";
                            else if (r.LicenseType == "FITNESS") licenseName = "Fitness License (By RTO)";
                            else if (r.LicenseType == "EXPLOSIVE") licenseName = "Explosive License";
                            else if (r.LicenseType == "GREEN_CARD") licenseName = "Green Card";
                            else if (r.LicenseType == "INSURANCE") licenseName = "Vehicle Insurance";
                            else if (r.LicenseType == "CALIBRATION") licenseName = "Calibration Certificate";
                            
                            C(licenseName);
                            C(r.LicenseNumber ?? "PENDING");
                            C(r.IssuingAuthority ?? "N/A");
                            
                            var formattedExpiry = "PENDING";
                            if (!string.IsNullOrEmpty(r.ExpiryDate) && DateTime.TryParse(r.ExpiryDate, out var dt))
                            {
                                formattedExpiry = dt.ToString("dd-MMM-yyyy");
                            }
                            
                            C(formattedExpiry, fg);
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

        return doc.GeneratePdf();
    }
}
