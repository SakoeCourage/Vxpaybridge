using Carter;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VxPayBridge.API.Database;
using VxPayBridge.API.Database.Entities;
using VxPayBridge.API.SharedServices.Security;

namespace VxPayBridge.API.Domain.Auth;

public class CreateUserRequest
{
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string TelephoneNumber { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class UserResponse
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string TelephoneNumber { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class MapManageUsersEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/internal/users",
            async (DatabaseContext dbContext) =>
            {
                var users = await dbContext.AppUsers
                    .OrderBy(u => u.UserName)
                    .Select(u => new UserResponse
                    {
                        Id = u.ID,
                        UserName = u.UserName,
                        Email = u.Email,
                        TelephoneNumber = u.TelephoneNumber,
                        IsActive = u.IsActive
                    })
                    .ToListAsync();

                return Results.Ok(users);
            })
            .WithTags("Internal Users")
            .WithName("GetInternalUsers")
            .Produces<List<UserResponse>>(StatusCodes.Status200OK);

        app.MapPost("/api/internal/users",
            async ([FromBody] CreateUserRequest request, DatabaseContext dbContext) =>
            {
                var validationError = Validate(request);
                if (validationError != null)
                {
                    return Results.BadRequest(new { error = validationError });
                }

                var email = request.Email.Trim().ToLowerInvariant();
                var userName = request.UserName.Trim();
                var telephoneNumber = request.TelephoneNumber.Trim();

                var exists = await dbContext.AppUsers.AnyAsync(u =>
                    u.Email == email ||
                    u.UserName == userName ||
                    u.TelephoneNumber == telephoneNumber);

                if (exists)
                {
                    return Results.Conflict(new { error = "A user with this username, email, or telephone number already exists" });
                }

                var user = new AppUser
                {
                    ID = Guid.NewGuid(),
                    UserName = userName,
                    Email = email,
                    TelephoneNumber = telephoneNumber,
                    PasswordHash = HmacHelper.HashSecret(request.Password),
                    CreatedAt = DateTime.UtcNow
                };

                dbContext.AppUsers.Add(user);
                await dbContext.SaveChangesAsync();
                return Results.Ok(ToResponse(user));
            })
            .WithTags("Internal Users")
            .WithName("CreateInternalUser")
            .Produces<UserResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict);

        app.MapPatch("/api/internal/users/{id:guid}/status",
            async (Guid id, [FromBody] UpdateUserStatusRequest request, DatabaseContext dbContext) =>
            {
                var user = await dbContext.AppUsers.FirstOrDefaultAsync(u => u.ID == id);
                if (user == null)
                {
                    return Results.NotFound();
                }

                user.IsActive = request.IsActive;
                user.UpdatedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync();
                return Results.Ok(ToResponse(user));
            })
            .WithTags("Internal Users")
            .WithName("UpdateInternalUserStatus")
            .Produces<UserResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static UserResponse ToResponse(AppUser user)
    {
        return new UserResponse
        {
            Id = user.ID,
            UserName = user.UserName,
            Email = user.Email,
            TelephoneNumber = user.TelephoneNumber,
            IsActive = user.IsActive
        };
    }

    private static string? Validate(CreateUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserName)) return "UserName is required";
        if (string.IsNullOrWhiteSpace(request.Email)) return "Email is required";
        if (string.IsNullOrWhiteSpace(request.TelephoneNumber)) return "TelephoneNumber is required";
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8) return "Password must be at least 8 characters";
        return null;
    }
}

public class UpdateUserStatusRequest
{
    public bool IsActive { get; set; }
}
