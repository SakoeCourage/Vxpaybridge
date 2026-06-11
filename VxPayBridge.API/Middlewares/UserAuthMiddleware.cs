using Microsoft.EntityFrameworkCore;
using VxPayBridge.API.Database;
using VxPayBridge.API.Shared;
using VxPayBridge.API.SharedServices.Auth;

namespace VxPayBridge.API.Middlewares;

public class UserAuthMiddleware
{
    private readonly RequestDelegate _next;

    public UserAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, DatabaseContext dbContext, UserTokenService tokenService)
    {
        if (!context.Request.Path.StartsWithSegments("/api/internal"))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("Authorization", out var authorizationValues))
        {
            await RespondUnauthorized(context, "Missing bearer token");
            return;
        }

        var authorization = authorizationValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authorization) ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await RespondUnauthorized(context, "Invalid authorization header");
            return;
        }

        var token = authorization["Bearer ".Length..].Trim();
        if (!tokenService.TryValidateToken(token, out var userId))
        {
            await RespondUnauthorized(context, "Invalid or expired token");
            return;
        }

        var user = await dbContext.AppUsers.FirstOrDefaultAsync(u => u.ID == userId && u.IsActive);
        if (user == null)
        {
            await RespondUnauthorized(context, "User is inactive or no longer exists");
            return;
        }

        context.Items["UserId"] = user.ID;
        await _next(context);
    }

    private static async Task RespondUnauthorized(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(Error.Unauthorized(message));
    }
}

public static class UserAuthMiddlewareExtensions
{
    public static IApplicationBuilder UseUserAuthentication(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<UserAuthMiddleware>();
    }
}
