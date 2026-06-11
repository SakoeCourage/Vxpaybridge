using Carter;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VxPayBridge.API.Database;
using VxPayBridge.API.Shared;

namespace VxPayBridge.API.Domain.Payments;

public static class GetPaymentStatus
{
    public class GetPaymentStatusRequest : IRequest<Result<GetPaymentStatusResponse>>
    {
        public string ClientReference { get; set; } = string.Empty;
    }

    public class GetPaymentStatusResponse
    {
        public string ClientReference { get; set; } = string.Empty;
        public string GatewayTransactionId { get; set; } = string.Empty;
        public string AudType { get; set; } = string.Empty;
        public string AudId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string? AuthorizationUrl { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    internal sealed class Handler : IRequestHandler<GetPaymentStatusRequest, Result<GetPaymentStatusResponse>>
    {
        private readonly DatabaseContext _dbContext;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public Handler(DatabaseContext dbContext, IHttpContextAccessor httpContextAccessor)
        {
            _dbContext = dbContext;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<Result<GetPaymentStatusResponse>> Handle(GetPaymentStatusRequest request, CancellationToken cancellationToken)
        {
            var clientAppIdObj = _httpContextAccessor.HttpContext?.Items["ClientAppId"];
            if (clientAppIdObj == null || !Guid.TryParse(clientAppIdObj.ToString(), out var clientAppId))
            {
                return Result.Failure<GetPaymentStatusResponse>(Error.Unauthorized("Unauthenticated Client"));
            }

            // Scope query to the authenticated client's transactions only
            var transaction = await _dbContext.PaymentTransactions
                .FirstOrDefaultAsync(
                    t => t.ClientReference == request.ClientReference && t.ClientAppID == clientAppId,
                    cancellationToken);

            if (transaction == null)
            {
                return Result.Failure<GetPaymentStatusResponse>(
                    Error.NotFound($"No payment found with ClientReference '{request.ClientReference}'."));
            }

            return Result.Success(new GetPaymentStatusResponse
            {
                ClientReference = transaction.ClientReference,
                GatewayTransactionId = transaction.GatewayTransactionID,
                AudType = transaction.AudType,
                AudId = transaction.AudID,
                Status = transaction.Status,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                AuthorizationUrl = transaction.AuthorizationUrl,
                CreatedAt = transaction.CreatedAt,
                UpdatedAt = transaction.UpdatedAt
            });
        }
    }
}

public class MapGetPaymentStatusEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/payments/status/{clientReference}", async (
            string clientReference,
            [FromServices] ISender sender) =>
        {
            var request = new GetPaymentStatus.GetPaymentStatusRequest
            {
                ClientReference = clientReference
            };

            var response = await sender.Send(request);

            if (response.IsFailure)
            {
                return response.Error.Code == "404"
                    ? Results.NotFound(response.Error)
                    : Results.BadRequest(response.Error);
            }

            return Results.Ok(response.Value);
        })
        .WithName("GetPaymentStatus")
        .Produces<GetPaymentStatus.GetPaymentStatusResponse>(StatusCodes.Status200OK)
        .Produces<Error>(StatusCodes.Status400BadRequest)
        .Produces<Error>(StatusCodes.Status404NotFound);
    }
}
