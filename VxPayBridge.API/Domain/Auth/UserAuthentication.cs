using Carter;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using VxPayBridge.API.Database;
using VxPayBridge.API.Database.Entities;
using VxPayBridge.API.SharedServices.Auth;
using VxPayBridge.API.SharedServices.Security;
using VxPayBridge.API.SharedServices.Sms;

namespace VxPayBridge.API.Domain.Auth;

public class LoginRequest
{
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class VerifyOtpRequest
{
    public string Login { get; set; } = string.Empty;
    public string Otp { get; set; } = string.Empty;
}

public class LoginResponse
{
    public bool OtpRequired { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class VerifyOtpResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

public class MapUserAuthenticationEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login",
            async ([FromBody] LoginRequest request, DatabaseContext dbContext, ISmsService smsService) =>
            {
                var login = request.Login.Trim().ToLowerInvariant();
                var user = await dbContext.AppUsers.FirstOrDefaultAsync(u =>
                    u.IsActive &&
                    (u.Email.ToLower() == login ||
                     u.UserName.ToLower() == login ||
                     u.TelephoneNumber == request.Login.Trim()));

                if (user == null || !HmacHelper.VerifySecret(request.Password, user.PasswordHash))
                {
                    return Results.Unauthorized();
                }

                var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
                dbContext.UserOtps.Add(new UserOtp
                {
                    ID = Guid.NewGuid(),
                    UserID = user.ID,
                    CodeHash = HmacHelper.HashSecret(otp),
                    ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                    CreatedAt = DateTime.UtcNow
                });
                await dbContext.SaveChangesAsync();

                smsService.QueueSms(user.TelephoneNumber, $"Your VxPayBridge login OTP is {otp}. It expires in 10 minutes.");

                return Results.Ok(new LoginResponse
                {
                    OtpRequired = true,
                    Message = "OTP sent to the user's telephone number"
                });
            })
            .WithTags("Auth")
            .WithName("LoginUser");

        app.MapPost("/api/auth/verify-otp",
            async ([FromBody] VerifyOtpRequest request, DatabaseContext dbContext, UserTokenService tokenService, IConfiguration configuration) =>
            {
                var login = request.Login.Trim().ToLowerInvariant();
                var user = await dbContext.AppUsers.FirstOrDefaultAsync(u =>
                    u.IsActive &&
                    (u.Email.ToLower() == login ||
                     u.UserName.ToLower() == login ||
                     u.TelephoneNumber == request.Login.Trim()));

                if (user == null)
                {
                    return Results.Unauthorized();
                }

                var otp = await dbContext.UserOtps
                    .Where(o => o.UserID == user.ID && o.UsedAt == null && o.ExpiresAt > DateTime.UtcNow)
                    .OrderByDescending(o => o.CreatedAt)
                    .FirstOrDefaultAsync();

                if (otp == null)
                {
                    return Results.Unauthorized();
                }

                if (otp.FailedAttempts >= 5 || !HmacHelper.VerifySecret(request.Otp, otp.CodeHash))
                {
                    otp.FailedAttempts += 1;
                    await dbContext.SaveChangesAsync();
                    return Results.Unauthorized();
                }

                otp.UsedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync();

                var accessToken = tokenService.CreateToken(user.ID, user.Email);
                var expiresAt = DateTime.UtcNow.AddMinutes(Math.Max(5, configuration.GetValue<int?>("Auth:AccessTokenMinutes") ?? 60));
                return Results.Ok(new VerifyOtpResponse
                {
                    AccessToken = accessToken,
                    ExpiresAt = expiresAt
                });
            })
            .WithTags("Auth")
            .WithName("VerifyUserOtp");
    }
}
