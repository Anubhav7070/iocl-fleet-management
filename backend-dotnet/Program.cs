using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using IoclFleetApi.Data;
using IoclFleetApi.Hubs;
using IoclFleetApi.Middleware;
using IoclFleetApi.Services;

// ─── Configure QuestPDF License (Community) ─────────────────────────────
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// ─── Database (SQLite via EF Core) ──────────────────────────────────────
var dbPath = Path.GetFullPath(builder.Configuration["Database:StoragePath"] ?? "./database/iocl_compliance.sqlite");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// ─── JWT Authentication ─────────────────────────────────────────────────
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "iocl_panipat_refinery_fleet_secret_key_2026_xyz";
var key = Encoding.UTF8.GetBytes(jwtSecret);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ClockSkew = TimeSpan.Zero
    };
    // Support JWT token for SignalR (passed as query param)
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization();

// ─── Rate Limiting ──────────────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("general", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(15)
            }));
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(15)
            }));
});

// ─── Services (DI) ──────────────────────────────────────────────────────
builder.Services.AddSingleton<IJwtService, JwtService>();
builder.Services.AddSingleton<IComplianceAlertDispatcher, ComplianceAlertDispatcher>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IComplianceService, ComplianceService>();
builder.Services.AddSingleton<IEmailService, EmailService>();
builder.Services.AddHostedService<ComplianceCheckHostedService>();

// ─── SignalR ────────────────────────────────────────────────────────────
builder.Services.AddSignalR();

// ─── CORS ───────────────────────────────────────────────────────────────
var origins = (builder.Configuration["Frontend:Url"] ?? "http://localhost:5173")
    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(origins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// ─── Controllers ────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        opts.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

// ─── Build App ──────────────────────────────────────────────────────────
var app = builder.Build();

// ─── Seed database if --seed flag is passed ─────────────────────────────
if (args.Contains("--seed"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    await DbSeeder.SeedAsync(db, config);
    Console.WriteLine("\n[Server] Database seeded successfully. You can now run the server without --seed.");
    return;
}

// ─── Ensure database exists ─────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// ─── Middleware Pipeline ────────────────────────────────────────────────
app.UseMiddleware<ExceptionMiddleware>();
app.UseCors();
app.UseRateLimiter();

// Serve uploaded files
var uploadDir = Path.GetFullPath(builder.Configuration["Upload:Directory"] ?? "./uploads");
Directory.CreateDirectory(uploadDir);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadDir),
    RequestPath = "/uploads",
    OnPrepareResponse = ctx =>
    {
        var origin = ctx.Context.Request.Headers["Origin"].ToString();
        if (!string.IsNullOrEmpty(origin))
        {
            ctx.Context.Response.Headers.Append("Access-Control-Allow-Origin", origin);
            ctx.Context.Response.Headers.Append("Access-Control-Allow-Headers", "Origin, X-Requested-With, Content-Type, Accept, Authorization");
            ctx.Context.Response.Headers.Append("Access-Control-Allow-Methods", "GET, OPTIONS");
            ctx.Context.Response.Headers.Append("Access-Control-Allow-Credentials", "true");
        }
    }
});

// Serve frontend dist (if present)
var wwwrootPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(wwwrootPath))
{
    app.UseStaticFiles();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ComplianceHub>("/hubs/compliance");

// Serve frontend SPA fallback for non-API routes
app.MapFallbackToFile("index.html");

var port = builder.Configuration["Port"] ?? "5000";
app.Urls.Add($"http://0.0.0.0:{port}");

Console.WriteLine($"\n[Server] IOCL Fleet Compliance API (ASP.NET Core) running on port {port}");
Console.WriteLine($"[Server] Database: {dbPath}");
Console.WriteLine($"[Server] Frontend: {string.Join(", ", origins)}");
Console.WriteLine($"[Server] SignalR Hub: /hubs/compliance");

app.Run();
