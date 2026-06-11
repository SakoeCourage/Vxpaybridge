using Carter;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VxPayBridge.API.Database;
using VxPayBridge.API.Database.Entities;
using VxPayBridge.API.Shared;
using VxPayBridge.API.SharedServices.Security;

namespace VxPayBridge.API.Domain.Clients;

public static class CreateClientApp
{
    public class CreateClientAppRequest : IRequest<Result<CreateClientAppResponse>>
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string WebhookUrl { get; set; } = string.Empty;
    }

    public class CreateClientAppResponse
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
    }

    public class Validator : AbstractValidator<CreateClientAppRequest>
    {
        public Validator()
        {
            RuleFor(x => x.Code).NotEmpty().WithMessage("Code is required");
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required");
            RuleFor(x => x.WebhookUrl).NotEmpty().WithMessage("WebhookUrl is required");
        }
    }

    internal sealed class Handler : IRequestHandler<CreateClientAppRequest, Result<CreateClientAppResponse>>
    {
        private readonly DatabaseContext _dbContext;

        public Handler(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<Result<CreateClientAppResponse>> Handle(CreateClientAppRequest request, CancellationToken cancellationToken)
        {
            // Generate ClientId and ClientSecret
            var clientId = $"client_{Guid.NewGuid():N}";
            var clientSecret = $"sec_{Guid.NewGuid():N}{Guid.NewGuid():N}";

            // Hash the secret before storing — the raw secret is only returned once and never persisted.
            var clientSecretHash = HmacHelper.HashSecret(clientSecret);

            var clientApp = new ClientApp
            {
                ID = Guid.NewGuid(),
                Code = request.Code,
                Name = request.Name,
                WebhookUrl = request.WebhookUrl,
                ClientId = clientId,
                ClientSecretHash = clientSecretHash,
                WebhookSigningSecret = clientSecret,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.ClientApps.Add(clientApp);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result.Success(new CreateClientAppResponse
            {
                ClientId = clientId,
                ClientSecret = clientSecret
            });
        }
    }
}

public class MapCreateClientAppEndpoint : ICarterModule
{
    public class UpdateClientStatusRequest
    {
        public bool IsActive { get; set; }
    }

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/internal/clients", async (
            [FromBody] CreateClientApp.CreateClientAppRequest request,
            [FromServices] ISender sender,
            [FromServices] IValidator<CreateClientApp.CreateClientAppRequest> validator) =>
        {
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                var errors = string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage));
                return Results.UnprocessableEntity(Error.BadRequest(errors));
            }

            var response = await sender.Send(request);

            if (response.IsFailure)
            {
                return Results.BadRequest(response.Error);
            }

            return Results.Ok(response.Value);
        })
        .WithName("CreateClientApp")
        .Produces<CreateClientApp.CreateClientAppResponse>(StatusCodes.Status200OK)
        .Produces<Error>(StatusCodes.Status400BadRequest)
        .Produces<Error>(StatusCodes.Status422UnprocessableEntity);

        app.MapPatch("/api/internal/clients/{id:guid}/status", async (
            Guid id,
            [FromBody] UpdateClientStatusRequest request,
            DatabaseContext dbContext) =>
        {
            var clientApp = await dbContext.ClientApps.FirstOrDefaultAsync(c => c.ID == id);
            if (clientApp == null)
            {
                return Results.NotFound();
            }

            clientApp.IsActive = request.IsActive;
            clientApp.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();

            return Results.Ok(new
            {
                clientApp.ID,
                clientApp.Code,
                clientApp.Name,
                clientApp.ClientId,
                clientApp.IsActive,
                clientApp.UpdatedAt
            });
        })
        .WithName("UpdateClientAppStatus")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);
    }
}
