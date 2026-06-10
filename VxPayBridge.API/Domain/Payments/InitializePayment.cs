using Carter;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VxPayBridge.API.Database;
using VxPayBridge.API.Database.Entities;
using VxPayBridge.API.Shared;
using VxPayBridge.API.SharedServices.Providers;

namespace VxPayBridge.API.Domain.Payments;

public static class InitializePayment
{
    public class InitializePaymentRequest : IRequest<Result<PaymentInitializationResponse>>
    {
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "GHS";
        public string ClientReference { get; set; } = string.Empty;
        public string ClientEmail { get; set; } = string.Empty;
        public string CallbackUrl { get; set; } = string.Empty;
    }

    public class Validator : AbstractValidator<InitializePaymentRequest>
    {
        public Validator()
        {
            RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Amount must be greater than zero");
            RuleFor(x => x.Currency).NotEmpty().WithMessage("Currency is required");
            RuleFor(x => x.ClientReference).NotEmpty().WithMessage("ClientReference is required");
            RuleFor(x => x.ClientEmail).NotEmpty().EmailAddress().WithMessage("Valid ClientEmail is required");
        }
    }

    internal sealed class Handler : IRequestHandler<InitializePaymentRequest, Result<PaymentInitializationResponse>>
    {
        private readonly DatabaseContext _dbContext;
        private readonly IPaymentProvider _paymentProvider;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public Handler(DatabaseContext dbContext, IPaymentProvider paymentProvider, IHttpContextAccessor httpContextAccessor)
        {
            _dbContext = dbContext;
            _paymentProvider = paymentProvider;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<Result<PaymentInitializationResponse>> Handle(InitializePaymentRequest request, CancellationToken cancellationToken)
        {
            var clientAppIdObj = _httpContextAccessor.HttpContext?.Items["ClientAppId"];
            if (clientAppIdObj == null || !Guid.TryParse(clientAppIdObj.ToString(), out var clientAppId))
            {
                return Result.Failure<PaymentInitializationResponse>(Error.Unauthorized("Unauthenticated Client"));
            }

            var clientApp = await _dbContext.ClientApps.FindAsync(new object[] { clientAppId }, cancellationToken);
            if (clientApp == null)
            {
                return Result.Failure<PaymentInitializationResponse>(Error.NotFound("ClientApp not found"));
            }

            var existingTransaction = await _dbContext.PaymentTransactions
                .FirstOrDefaultAsync(t => t.ClientReference == request.ClientReference && t.ClientAppID == clientAppId, cancellationToken);

            if (existingTransaction != null)
            {
                if (existingTransaction.Status == "PENDING" || existingTransaction.Status == "INITIALIZING")
                {
                    // Return existing URLs if already initialized
                    if (!string.IsNullOrEmpty(existingTransaction.AuthorizationUrl))
                    {
                        return Result.Success(new PaymentInitializationResponse
                        {
                            AuthorizationUrl = existingTransaction.AuthorizationUrl,
                            AccessCode = existingTransaction.AccessCode ?? string.Empty,
                            Reference = existingTransaction.GatewayTransactionID
                        });
                    }
                }
                
                return Result.Failure<PaymentInitializationResponse>(Error.BadRequest("Transaction already processed or in an invalid state for retry."));
            }

            // Create a local PaymentTransaction record as DRAFT/INITIALIZING
            var transaction = new PaymentTransaction
            {
                ID = Guid.NewGuid(),
                ClientAppID = clientAppId,
                ClientReference = request.ClientReference,
                Amount = request.Amount,
                Currency = request.Currency,
                Status = "INITIALIZING",
                CreatedAt = DateTime.UtcNow,
                ClientApp = clientApp
            };

            // Assuming Paystack provides a reference or we generate one
            transaction.GatewayTransactionID = $"TRX-{Guid.NewGuid():N}"; 

            _dbContext.PaymentTransactions.Add(transaction);
            await _dbContext.SaveChangesAsync(cancellationToken); // Safe local state first

            var initResult = await _paymentProvider.InitializePaymentAsync(transaction, request.ClientEmail, request.CallbackUrl);

            if (initResult.IsFailure)
            {
                transaction.Status = "FAILED";
                transaction.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
                return initResult;
            }

            // Update with success details
            transaction.Status = "PENDING";
            transaction.AuthorizationUrl = initResult.Value?.AuthorizationUrl;
            transaction.AccessCode = initResult.Value?.AccessCode;
            transaction.UpdatedAt = DateTime.UtcNow;
            
            await _dbContext.SaveChangesAsync(cancellationToken);

            return initResult;
        }
    }
}

public class MapInitializePaymentEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/payments/initialize", async (
            [FromBody] InitializePayment.InitializePaymentRequest request,
            [FromServices] ISender sender,
            [FromServices] IValidator<InitializePayment.InitializePaymentRequest> validator) =>
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
        .WithName("InitializePayment")
        .Produces<PaymentInitializationResponse>(StatusCodes.Status200OK)
        .Produces<Error>(StatusCodes.Status400BadRequest)
        .Produces<Error>(StatusCodes.Status422UnprocessableEntity);
    }
}
