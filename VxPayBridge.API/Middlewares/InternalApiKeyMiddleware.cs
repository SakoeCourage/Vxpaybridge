using VxPayBridge.API.Shared;

namespace VxPayBridge.API.Middlewares;

/// <summary>
/// Protects /api/internal/** routes with a static API key set in appsettings.
/// These routes are for VxPayBridge administrators only (e.g., registering new client apps).
/// </summary>
public class InternalApiKeyMiddleware
{
    private const string ApiKeyHeader = "x-internal-api-key";
    private readonly RequestDelegate _next;

    public InternalApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IConfiguration configuration)
    {
        if (!context.Request.Path.StartsWithSegments("/api/internal"))
        {
            await _next(context);
            return;
        }

        var expectedApiKey = configuration["InternalApiKey"];
        if (string.IsNullOrEmpty(expectedApiKey))
        {
            // Fail closed: if no key is configured, deny all internal access
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(
                new Error("503", "Internal API key is not configured on the server."));
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var providedKey) ||
            !string.Equals(providedKey, expectedApiKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(Error.Unauthorized("Invalid or missing internal API key."));
            return;
        }

        await _next(context);
    }
}

public static class InternalApiKeyMiddlewareExtensions
{
    public static IApplicationBuilder UseInternalApiKeyAuthentication(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<InternalApiKeyMiddleware>();
    }
}
