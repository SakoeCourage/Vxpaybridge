using System.Text.Json;
using System.Text.Json.Nodes;
using Carter;
using Hangfire;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VxPayBridge.API.Database;
using VxPayBridge.API.Database.Entities;
using VxPayBridge.API.Shared;
using VxPayBridge.API.SharedServices.Security;
using VxPayBridge.API.SharedServices.Webhooks;

namespace VxPayBridge.API.Domain.Webhooks;

public static class PaystackWebhook
{
    public class PaystackWebhookRequest : IRequest<Result<string>>
    {
        public string RawBody { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
    }

    internal sealed class Handler : IRequestHandler<PaystackWebhookRequest, Result<string>>
    {
        private readonly DatabaseContext _dbContext;
        private readonly IConfiguration _configuration;
        private readonly ILogger<Handler> _logger;
        private readonly IBackgroundJobClient _backgroundJobClient;

        public Handler(DatabaseContext dbContext, IConfiguration configuration, ILogger<Handler> logger, IBackgroundJobClient backgroundJobClient)
        {
            _dbContext = dbContext;
            _configuration = configuration;
            _logger = logger;
            _backgroundJobClient = backgroundJobClient;
        }

        public async Task<Result<string>> Handle(PaystackWebhookRequest request, CancellationToken cancellationToken)
        {
            var secretKey = _configuration["Paystack:SecretKey"];
            if (string.IsNullOrEmpty(secretKey))
            {
                _logger.LogError("Paystack secret key is missing");
                return Result.Failure<string>(new Error("Configuration", "Server configuration error"));
            }

            // Paystack signs webhooks with HMAC SHA-512 using the Paystack secret key.
            if (!HmacHelper.ValidateSha512Signature(secretKey, request.RawBody, request.Signature))
            {
                _logger.LogWarning("Invalid Paystack webhook signature");
                return Result.Failure<string>(Error.Unauthorized("Invalid signature"));
            }

            try
            {
                var payload = JsonNode.Parse(request.RawBody);
                var metadata = payload?["data"]?["metadata"];
                
                if (metadata == null)
                {
                    _logger.LogWarning("Webhook missing metadata");
                    return Result.Success("Ignored: No metadata");
                }

                var clientCodeNode = metadata["gateway_client_code"];
                var gatewayTxNode = metadata["gateway_transaction_id"];

                if (clientCodeNode == null || gatewayTxNode == null)
                {
                    _logger.LogWarning("Webhook missing required metadata fields");
                    return Result.Success("Ignored: Missing required metadata");
                }

                var clientCode = clientCodeNode.ToString();
                var gatewayTransactionId = gatewayTxNode.ToString();
                var eventType = payload?["event"]?.ToString() ?? "unknown";

                // Idempotency Check: Did we already process this exact event for this transaction?
                var alreadyProcessed = await _dbContext.WebhookEvents
                    .AnyAsync(w => w.GatewayTransactionID == gatewayTransactionId && w.EventType == eventType, cancellationToken);
                
                if (alreadyProcessed)
                {
                    _logger.LogInformation("Ignored duplicate webhook event {EventType} for {GatewayTransactionID}", eventType, gatewayTransactionId);
                    return Result.Success("Ignored: Duplicate webhook");
                }

                var clientApp = await _dbContext.ClientApps.FirstOrDefaultAsync(c => c.Code == clientCode, cancellationToken);
                if (clientApp == null)
                {
                    _logger.LogWarning("Webhook client code {ClientCode} not found", clientCode);
                    return Result.Success("Ignored: Unknown client code");
                }

                // Update Transaction Status
                var transaction = await _dbContext.PaymentTransactions
                    .FirstOrDefaultAsync(t => t.GatewayTransactionID == gatewayTransactionId, cancellationToken);
                
                if (transaction != null)
                {
                    if (eventType == "charge.success")
                    {
                        transaction.Status = "SUCCESS";
                    }
                    else if (eventType == "charge.failed")
                    {
                        transaction.Status = "FAILED";
                    }
                    transaction.UpdatedAt = DateTime.UtcNow;
                }

                // Store Webhook Event
                var webhookEvent = new WebhookEvent
                {
                    ID = Guid.NewGuid(),
                    ClientAppID = clientApp.ID,
                    GatewayTransactionID = gatewayTransactionId,
                    EventType = eventType,
                    RawPayload = request.RawBody,
                    Status = "PENDING",
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.WebhookEvents.Add(webhookEvent);
                await _dbContext.SaveChangesAsync(cancellationToken);

                // Queue Background Job for Delivery
                _backgroundJobClient.Enqueue<WebhookDeliveryService>(service => service.DeliverWebhookAsync(webhookEvent.ID));

                return Result.Success("Webhook processed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook");
                return Result.Failure<string>(new Error("Webhook.Error", "Error processing webhook"));
            }
        }
    }
}

public class MapPaystackWebhookEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/webhooks/paystack", async (
            HttpContext context,
            [FromServices] ISender sender) =>
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body);
            var rawBody = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;

            if (!context.Request.Headers.TryGetValue("x-paystack-signature", out var signatureValues))
            {
                return Results.Unauthorized();
            }

            var request = new PaystackWebhook.PaystackWebhookRequest
            {
                RawBody = rawBody,
                Signature = signatureValues.FirstOrDefault() ?? string.Empty
            };

            var response = await sender.Send(request);

            if (response.IsFailure)
            {
                // Paystack retries if we don't return 200, so we only return 400 if it's explicitly a signature error
                if (response.Error.Code == "401") return Results.Unauthorized();
                return Results.BadRequest(response.Error);
            }

            return Results.Ok();
        })
        .WithName("PaystackWebhook")
        .ExcludeFromDescription();
    }
}
