using Microsoft.EntityFrameworkCore;
using VxPayBridge.API.Database;
using VxPayBridge.API.Shared;
using VxPayBridge.API.SharedServices.Security;

namespace VxPayBridge.API.Middlewares;

public class ClientAuthMiddleware
{
    private readonly RequestDelegate _next;

    public ClientAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, DatabaseContext dbContext)
    {
        // Skip authentication for webhooks since they are authenticated via signature
        if (context.Request.Path.StartsWithSegments("/api/webhooks"))
        {
            await _next(context);
            return;
        }

        // Skip authentication for internal admin APIs (like creating a client)
        if (context.Request.Path.StartsWithSegments("/api/internal"))
        {
            await _next(context);
            return;
        }

        if (context.Request.Path.StartsWithSegments("/api/auth"))
        {
            await _next(context);
            return;
        }

        // Only authenticate /api endpoints
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("x-client-id", out var clientIdValues) ||
            !context.Request.Headers.TryGetValue("x-client-secret", out var clientSecretValues))
        {
            await RespondUnauthorized(context, "Missing client credentials");
            return;
        }

        var clientId = clientIdValues.FirstOrDefault();
        var clientSecret = clientSecretValues.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            await RespondUnauthorized(context, "Invalid client credentials format");
            return;
        }

        var clientApp = await dbContext.ClientApps.FirstOrDefaultAsync(c => c.ClientId == clientId && c.IsActive);
        if (clientApp == null)
        {
            await RespondUnauthorized(context, "Invalid Client ID or Inactive Client");
            return;
        }

        // Constant-time hash comparison to prevent timing attacks.
        if (!HmacHelper.VerifySecret(clientSecret, clientApp.ClientSecretHash))
        {
            await RespondUnauthorized(context, "Invalid Client Secret");
            return;
        }

        // Store client app ID in HttpContext items for later use
        context.Items["ClientAppId"] = clientApp.ID;

        await _next(context);
    }

    private static async Task RespondUnauthorized(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        var error = Error.Unauthorized(message);
        await context.Response.WriteAsJsonAsync(error);
    }
}

public static class ClientAuthMiddlewareExtensions
{
    public static IApplicationBuilder UseClientAuthentication(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ClientAuthMiddleware>();
    }
}
