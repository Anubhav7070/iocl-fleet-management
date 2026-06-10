using System.Net;
using System.Text.Json;

namespace IoclFleetApi.Middleware;

/// <summary>
/// Global exception handler matching the Express error middleware response shapes.
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ErrorHandler] Exception caught");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var statusCode = (int)HttpStatusCode.InternalServerError;
        var message = "An unexpected server error occurred.";
        object? errors = null;

        // Handle specific exception types
        if (exception is Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
        {
            if (dbEx.InnerException?.Message.Contains("UNIQUE constraint failed") == true)
            {
                statusCode = 400;
                message = "Duplicate field value error.";
                errors = new[] { new { field = "unknown", message = dbEx.InnerException.Message } };
            }
        }

        context.Response.StatusCode = statusCode;

        var response = new
        {
            success = false,
            message,
            errors,
            timestamp = DateTime.UtcNow.ToString("o")
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}
