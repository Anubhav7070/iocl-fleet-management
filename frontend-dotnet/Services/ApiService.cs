using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using IoclFleetApi.DTOs;

namespace IoclFleetApi.Services;

public class ApiService
{
    private readonly HttpClient _http;
    private readonly ProtectedSessionStorage _sessionStorage;
    private readonly string _baseUrl;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ApiService(HttpClient http, ProtectedSessionStorage sessionStorage, IConfiguration config)
    {
        _http = http;
        _sessionStorage = sessionStorage;
        _baseUrl = config["Backend:Url"] ?? "http://localhost:5000";
    }

    private async Task<string?> GetTokenAsync()
    {
        try
        {
            var result = await _sessionStorage.GetAsync<string>("iocl_session_token");
            return result.Success ? result.Value : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task PrepareClientAsync()
    {
        _http.DefaultRequestHeaders.Authorization = null;
        var token = await GetTokenAsync();
        if (!string.IsNullOrEmpty(token))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private async Task<T> HandleResponseAsync<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            try
            {
                var failResult = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(JsonOptions);
                throw new HttpRequestException(failResult?.Message ?? "An error occurred during the API request.");
            }
            catch (JsonException)
            {
                throw new HttpRequestException($"HTTP Error {response.StatusCode}");
            }
        }

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOptions);
        if (result == null) throw new HttpRequestException("Failed to deserialize response.");
        return result.Data!;
    }

    // ─── Authentication ─────────────────────────────────────────────────────

    public async Task<LoginResponseDto> LoginAsync(string username, string password)
    {
        var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/auth/login", new LoginDto { Username = username, Password = password }, JsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            var failResult = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(JsonOptions);
            throw new Exception(failResult?.Message ?? "Login failed.");
        }
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponseDto>>(JsonOptions);
        if (result == null || result.Data == null) throw new Exception("Invalid login response.");
        
        await _sessionStorage.SetAsync("iocl_session_token", result.Data.Token);
        return result.Data;
    }

    public async Task<UserDto> GetMeAsync()
    {
        await PrepareClientAsync();
        var response = await _http.GetAsync($"{_baseUrl}/api/auth/me");
        var container = await HandleResponseAsync<UserContainerDto>(response);
        return container.User;
    }

    // ─── Vehicles ───────────────────────────────────────────────────────────

    public async Task<List<VehicleDto>> GetVehiclesAsync(Dictionary<string, string>? queryParams = null)
    {
        await PrepareClientAsync();
        var url = $"{_baseUrl}/api/vehicles";
        if (queryParams != null && queryParams.Count > 0)
        {
            var q = string.Join("&", queryParams.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}"));
            url += $"?{q}";
        }
        var response = await _http.GetAsync(url);
        return await HandleResponseAsync<List<VehicleDto>>(response);
    }

    public async Task<VehicleDto> GetVehicleAsync(int id)
    {
        await PrepareClientAsync();
        var response = await _http.GetAsync($"{_baseUrl}/api/vehicles/{id}");
        return await HandleResponseAsync<VehicleDto>(response);
    }

    public async Task<VehicleDto> CreateVehicleAsync(CreateVehicleDto dto)
    {
        await PrepareClientAsync();
        var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/vehicles", dto, JsonOptions);
        return await HandleResponseAsync<VehicleDto>(response);
    }

    public async Task<VehicleDto> CreateVehicleAsync(CreateVehicleDto dto, Stream rcStream, string rcFileName, string rcContentType,
        Dictionary<string, (Stream Stream, string FileName, string ContentType, string IssueDate, string ExpiryDate, string LicNo)> complianceDocs)
    {
        await PrepareClientAsync();
        using var content = new MultipartFormDataContent();

        // Core vehicle fields
        if (!string.IsNullOrEmpty(dto.VehicleNumber))
            content.Add(new StringContent(dto.VehicleNumber), "vehicleNumber");
        if (!string.IsNullOrEmpty(dto.VehicleType))
            content.Add(new StringContent(dto.VehicleType), "vehicleType");
        if (!string.IsNullOrEmpty(dto.DriverName))
            content.Add(new StringContent(dto.DriverName), "driverName");
        if (!string.IsNullOrEmpty(dto.VendorName))
            content.Add(new StringContent(dto.VendorName), "vendorName");
        if (dto.DepartmentId.HasValue)
            content.Add(new StringContent(dto.DepartmentId.Value.ToString()), "departmentId");

        // RC document
        var rcContent = new StreamContent(rcStream);
        rcContent.Headers.ContentType = new MediaTypeHeaderValue(rcContentType);
        content.Add(rcContent, "doc_RC", rcFileName);

        // 8 compliance documents with their expiry dates and license numbers
        foreach (var (type, doc) in complianceDocs)
        {
            var fileContent = new StreamContent(doc.Stream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(doc.ContentType);
            content.Add(fileContent, $"doc_{type}", doc.FileName);
            content.Add(new StringContent(doc.IssueDate), $"issue_{type}");
            content.Add(new StringContent(doc.ExpiryDate), $"expiry_{type}");
            content.Add(new StringContent(doc.LicNo), $"licNo_{type}");
        }

        var response = await _http.PostAsync($"{_baseUrl}/api/vehicles", content);
        return await HandleResponseAsync<VehicleDto>(response);
    }

    public async Task<VehicleDto> UpdateVehicleAsync(int id, UpdateVehicleDto dto)
    {
        await PrepareClientAsync();
        var response = await _http.PutAsJsonAsync($"{_baseUrl}/api/vehicles/{id}", dto, JsonOptions);
        return await HandleResponseAsync<VehicleDto>(response);
    }

    public async Task<VehicleDto> VerifyVehicleAsync(int id, bool isVerified)
    {
        await PrepareClientAsync();
        var response = await _http.PutAsJsonAsync($"{_baseUrl}/api/vehicles/{id}/verify", new { isVerified }, JsonOptions);
        return await HandleResponseAsync<VehicleDto>(response);
    }

    public async Task DeleteVehicleAsync(int id)
    {
        await PrepareClientAsync();
        var response = await _http.DeleteAsync($"{_baseUrl}/api/vehicles/{id}");
        if (!response.IsSuccessStatusCode)
        {
            var fail = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(JsonOptions);
            throw new Exception(fail?.Message ?? "Failed to delete vehicle.");
        }
    }

    // ─── Compliance ─────────────────────────────────────────────────────────

    public async Task<List<ComplianceRecordDto>> GetComplianceRecordsAsync(int? vehicleId = null)
    {
        await PrepareClientAsync();
        var url = $"{_baseUrl}/api/compliance";
        if (vehicleId.HasValue) url += $"?vehicleId={vehicleId.Value}";
        var response = await _http.GetAsync(url);
        return await HandleResponseAsync<List<ComplianceRecordDto>>(response);
    }

    public async Task<List<ComplianceRecordDto>> GetComplianceAlertsAsync(int? departmentId = null, string? priority = null)
    {
        await PrepareClientAsync();
        var url = $"{_baseUrl}/api/compliance/alerts";
        var query = new List<string>();
        if (departmentId.HasValue) query.Add($"departmentId={departmentId.Value}");
        if (!string.IsNullOrEmpty(priority)) query.Add($"priority={priority}");
        if (query.Count > 0) url += $"?{string.Join("&", query)}";
        
        var response = await _http.GetAsync(url);
        return await HandleResponseAsync<List<ComplianceRecordDto>>(response);
    }

    public async Task<List<RenewalHistoryDto>> GetRenewalHistoryAsync(int? vehicleId = null)
    {
        await PrepareClientAsync();
        var url = $"{_baseUrl}/api/compliance/history";
        if (vehicleId.HasValue) url += $"?vehicleId={vehicleId.Value}";
        var response = await _http.GetAsync(url);
        return await HandleResponseAsync<List<RenewalHistoryDto>>(response);
    }

    public async Task<object> RenewComplianceRecordAsync(int id, RenewComplianceDto dto, Stream fileStream, string fileName, string contentType)
    {
        await PrepareClientAsync();
        using var content = new MultipartFormDataContent();
        
        if (!string.IsNullOrEmpty(dto.LicenseNumber))
            content.Add(new StringContent(dto.LicenseNumber), "licenseNumber");
        if (!string.IsNullOrEmpty(dto.IssuingAuthority))
            content.Add(new StringContent(dto.IssuingAuthority), "issuingAuthority");
        if (!string.IsNullOrEmpty(dto.IssueDate))
            content.Add(new StringContent(dto.IssueDate), "issueDate");
        if (!string.IsNullOrEmpty(dto.ExpiryDate))
            content.Add(new StringContent(dto.ExpiryDate), "expiryDate");

        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);

        var response = await _http.PutAsync($"{_baseUrl}/api/compliance/renew/{id}", content);
        return await HandleResponseAsync<object>(response);
    }

    /// <summary>Sends a document to the backend for expiry date extraction via PdfPig.</summary>
    public async Task<(bool Found, string? Date, string Message)> ExtractDateFromDocumentAsync(Stream fileStream, string fileName, string contentType)
    {
        await PrepareClientAsync();
        using var content = new MultipartFormDataContent();
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);

        var response = await _http.PostAsync($"{_baseUrl}/api/compliance/extract-date", content);
        var json = await response.Content.ReadAsStringAsync();
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        bool found = root.TryGetProperty("found", out var f) && f.GetBoolean();
        string? date = root.TryGetProperty("date", out var d) ? d.GetString() : null;
        string msg = root.TryGetProperty("message", out var m) ? m.GetString() ?? string.Empty : string.Empty;
        return (found, date, msg);
    }


    public async Task<ComplianceRecordDto> VerifyComplianceDocumentAsync(int id, bool isVerified)
    {
        await PrepareClientAsync();
        var response = await _http.PutAsJsonAsync($"{_baseUrl}/api/compliance/{id}/verify", new VerifyDocumentDto { IsVerified = isVerified }, JsonOptions);
        return await HandleResponseAsync<ComplianceRecordDto>(response);
    }

    // ─── Departments ────────────────────────────────────────────────────────

    public async Task<List<DepartmentDto>> GetDepartmentsAsync()
    {
        await PrepareClientAsync();
        var response = await _http.GetAsync($"{_baseUrl}/api/departments");
        return await HandleResponseAsync<List<DepartmentDto>>(response);
    }

    public async Task<DepartmentDto> GetDepartmentAsync(int id)
    {
        await PrepareClientAsync();
        var response = await _http.GetAsync($"{_baseUrl}/api/departments/{id}");
        return await HandleResponseAsync<DepartmentDto>(response);
    }

    public async Task<DepartmentDto> CreateDepartmentAsync(CreateDepartmentDto dto)
    {
        await PrepareClientAsync();
        var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/departments", dto, JsonOptions);
        return await HandleResponseAsync<DepartmentDto>(response);
    }

    public async Task<DepartmentDto> UpdateDepartmentAsync(int id, UpdateDepartmentDto dto)
    {
        await PrepareClientAsync();
        var response = await _http.PutAsJsonAsync($"{_baseUrl}/api/departments/{id}", dto, JsonOptions);
        return await HandleResponseAsync<DepartmentDto>(response);
    }

    public async Task DeleteDepartmentAsync(int id)
    {
        await PrepareClientAsync();
        var response = await _http.DeleteAsync($"{_baseUrl}/api/departments/{id}");
        if (!response.IsSuccessStatusCode)
        {
            var fail = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(JsonOptions);
            throw new Exception(fail?.Message ?? "Failed to delete department.");
        }
    }

    // ─── Notifications ──────────────────────────────────────────────────────

    public async Task<List<NotificationDto>> GetNotificationsAsync()
    {
        await PrepareClientAsync();
        var response = await _http.GetAsync($"{_baseUrl}/api/notifications");
        return await HandleResponseAsync<List<NotificationDto>>(response);
    }

    public async Task ReadNotificationAsync(int id)
    {
        await PrepareClientAsync();
        var response = await _http.PutAsync($"{_baseUrl}/api/notifications/{id}/read", null);
        if (!response.IsSuccessStatusCode)
        {
            var fail = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(JsonOptions);
            throw new Exception(fail?.Message ?? "Failed to mark notification as read.");
        }
    }

    public async Task ReadAllNotificationsAsync()
    {
        await PrepareClientAsync();
        var response = await _http.PutAsync($"{_baseUrl}/api/notifications/read-all", null);
        if (!response.IsSuccessStatusCode)
        {
            var fail = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(JsonOptions);
            throw new Exception(fail?.Message ?? "Failed to mark all notifications as read.");
        }
    }

    // ─── Users (Super Admin) ────────────────────────────────────────────────

    public async Task<List<UserDto>> GetUsersAsync()
    {
        await PrepareClientAsync();
        var response = await _http.GetAsync($"{_baseUrl}/api/users");
        return await HandleResponseAsync<List<UserDto>>(response);
    }

    public async Task<UserDto> CreateUserAsync(CreateUserDto dto)
    {
        await PrepareClientAsync();
        var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/users", dto, JsonOptions);
        return await HandleResponseAsync<UserDto>(response);
    }

    public async Task<UserDto> UpdateUserAsync(int id, UpdateUserDto dto)
    {
        await PrepareClientAsync();
        var response = await _http.PutAsJsonAsync($"{_baseUrl}/api/users/{id}", dto, JsonOptions);
        return await HandleResponseAsync<UserDto>(response);
    }

    public async Task DeleteUserAsync(int id)
    {
        await PrepareClientAsync();
        var response = await _http.DeleteAsync($"{_baseUrl}/api/users/{id}");
        if (!response.IsSuccessStatusCode)
        {
            var fail = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(JsonOptions);
            throw new Exception(fail?.Message ?? "Failed to delete user.");
        }
    }

    // ─── Audit Logs ─────────────────────────────────────────────────────────

    public async Task<List<AuditLogDto>> GetAuditLogsAsync(Dictionary<string, string>? queryParams = null)
    {
        await PrepareClientAsync();
        var url = $"{_baseUrl}/api/audit";
        if (queryParams != null && queryParams.Count > 0)
        {
            var q = string.Join("&", queryParams.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}"));
            url += $"?{q}";
        }
        var response = await _http.GetAsync(url);
        return await HandleResponseAsync<List<AuditLogDto>>(response);
    }

    // ─── Dashboard Stats ────────────────────────────────────────────────────

    public async Task<DashboardStatsDto> GetDashboardStatsAsync()
    {
        await PrepareClientAsync();
        var response = await _http.GetAsync($"{_baseUrl}/api/dashboard/stats");
        return await HandleResponseAsync<DashboardStatsDto>(response);
    }

    public async Task<List<UploadedDocumentDto>> GetUploadedDocumentsAsync()
    {
        await PrepareClientAsync();
        var response = await _http.GetAsync($"{_baseUrl}/api/dashboard/uploaded-documents");
        return await HandleResponseAsync<List<UploadedDocumentDto>>(response);
    }

    // ─── File Downloads ─────────────────────────────────────────────────────

    public async Task<HttpResponseMessage> DownloadComplianceReportAsync(string format, string departmentId = "")
    {
        await PrepareClientAsync();
        var response = await _http.GetAsync($"{_baseUrl}/api/reports/compliance?format={format}&departmentId={departmentId}");
        if (!response.IsSuccessStatusCode) throw new Exception("Failed to download compliance report.");
        return response;
    }

    public async Task<HttpResponseMessage> DownloadExpiryReportAsync(string format)
    {
        await PrepareClientAsync();
        var response = await _http.GetAsync($"{_baseUrl}/api/reports/expiries?format={format}");
        if (!response.IsSuccessStatusCode) throw new Exception("Failed to download expiry report.");
        return response;
    }

    // ─── Admin Triggers ──────────────────────────────────────────────────────

    public async Task<bool> TriggerComplianceEmailsAsync()
    {
        await PrepareClientAsync();
        var response = await _http.PostAsync($"{_baseUrl}/api/admin/trigger-compliance-emails", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> TriggerDailyDigestAsync()
    {
        await PrepareClientAsync();
        var response = await _http.PostAsync($"{_baseUrl}/api/admin/trigger-daily-digest", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<VehicleDto> GetVehicleByPlateAsync(string plateNumber)
    {
        await PrepareClientAsync();
        var response = await _http.GetAsync($"{_baseUrl}/api/vehicles/verify/plate/{Uri.EscapeDataString(plateNumber)}");
        return await HandleResponseAsync<VehicleDto>(response);
    }

    public async Task LogGateEntryAsync(GateEntryLogDto dto)
    {
        await PrepareClientAsync();
        var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/vehicles/gate-entry/log", dto, JsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            var fail = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(JsonOptions);
            throw new Exception(fail?.Message ?? "Failed to log gate entry.");
        }
    }
}
