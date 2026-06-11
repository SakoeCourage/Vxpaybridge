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
using VxPayBridge.API.SharedServices.Ledger;
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
        private readonly LedgerService _ledgerService;

        public Handler(
            DatabaseContext dbContext,
            IConfiguration configuration,
            ILogger<Handler> logger,
            IBackgroundJobClient backgroundJobClient,
            LedgerService ledgerService)
        {
            _dbContext = dbContext;
            _configuration = configuration;
            _logger = logger;
            _backgroundJobClient = backgroundJobClient;
            _ledgerService = ledgerService;
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
                var eventType = payload?["event"]?.ToString() ?? "unknown";

                if (eventType.StartsWith("transfer.", StringComparison.OrdinalIgnoreCase))
                {
                    return await HandleTransferWebhookAsync(payload, eventType, request.RawBody, cancellationToken);
                }

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

                var clientApp = await _dbContext.ClientApps.FirstOrDefaultAsync(c => c.Code == clientCode, cancellationToken);
                if (clientApp == null)
                {
                    _logger.LogWarning("Webhook client code {ClientCode} not found", clientCode);
                    return Result.Success("Ignored: Unknown client code");
                }

                var alreadyProcessed = await _dbContext.WebhookEvents
                    .AnyAsync(w => w.GatewayTransactionID == gatewayTransactionId && w.EventType == eventType, cancellationToken);

                // Update Transaction Status
                var transaction = await _dbContext.PaymentTransactions
                    .FirstOrDefaultAsync(t => t.GatewayTransactionID == gatewayTransactionId, cancellationToken);

                await using var dbTransaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

                if (!alreadyProcessed)
                {
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
                }
                else
                {
                    _logger.LogInformation("Duplicate webhook event {EventType} for {GatewayTransactionID}; ensuring idempotent side effects", eventType, gatewayTransactionId);
                }

                if (transaction != null &&
                    eventType == "charge.success" &&
                    !string.IsNullOrWhiteSpace(transaction.AudType) &&
                    !string.IsNullOrWhiteSpace(transaction.AudID))
                {
                    await _ledgerService.CreditPaymentAsync(transaction, cancellationToken);
                }

                await dbTransaction.CommitAsync(cancellationToken);

                // Queue Background Job for Delivery
                var webhookEventId = await _dbContext.WebhookEvents
                    .Where(w => w.GatewayTransactionID == gatewayTransactionId && w.EventType == eventType)
                    .Select(w => w.ID)
                    .FirstOrDefaultAsync(cancellationToken);

                if (webhookEventId != Guid.Empty)
                {
                    _backgroundJobClient.Enqueue<WebhookDeliveryService>(service => service.DeliverWebhookAsync(webhookEventId));
                }

                return Result.Success("Webhook processed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook");
                return Result.Failure<string>(new Error("Webhook.Error", "Error processing webhook"));
            }
        }

        private async Task<Result<string>> HandleTransferWebhookAsync(
            JsonNode? payload,
            string eventType,
            string rawBody,
            CancellationToken cancellationToken)
        {
            var transferCode = payload?["data"]?["transfer_code"]?.ToString();
            if (string.IsNullOrWhiteSpace(transferCode))
            {
                _logger.LogWarning("Transfer webhook missing transfer_code");
                return Result.Success("Ignored: Missing transfer_code");
            }

            var withdrawal = await _dbContext.Withdrawals
                .FirstOrDefaultAsync(w => w.TransferCode == transferCode, cancellationToken);

            if (withdrawal == null)
            {
                _logger.LogWarning("Transfer webhook withdrawal not found for {TransferCode}", transferCode);
                return Result.Success("Ignored: Unknown transfer code");
            }

            var alreadyProcessed = await _dbContext.WebhookEvents
                .AnyAsync(w => w.GatewayTransactionID == transferCode && w.EventType == eventType, cancellationToken);

            await using var dbTransaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            if (!alreadyProcessed)
            {
                if (eventType == "transfer.success")
                {
                    if (withdrawal.Status is "QUEUED" or "PROCESSING" or "PENDING")
                    {
                        withdrawal.Status = "SUCCESS";
                        await _ledgerService.CompleteWithdrawalAsync(withdrawal, cancellationToken);
                    }
                }
                else if (eventType == "transfer.failed")
                {
                    if (withdrawal.Status is "QUEUED" or "PROCESSING" or "PENDING")
                    {
                        withdrawal.Status = "FAILED";
                        await _ledgerService.ReverseWithdrawalAsync(withdrawal, "WITHDRAWAL_FAILED", cancellationToken);
                    }
                }
                else if (eventType == "transfer.reversed")
                {
                    if (withdrawal.Status == "SUCCESS")
                    {
                        withdrawal.Status = "REVERSED";
                        await _ledgerService.ReverseCompletedWithdrawalAsync(withdrawal, cancellationToken);
                    }
                    else if (withdrawal.Status is "PENDING" or "PROCESSING")
                    {
                        withdrawal.Status = "REVERSED";
                        await _ledgerService.ReverseWithdrawalAsync(withdrawal, "WITHDRAWAL_REVERSED", cancellationToken);
                    }
                }

                withdrawal.UpdatedAt = DateTime.UtcNow;

                var webhookEvent = new WebhookEvent
                {
                    ID = Guid.NewGuid(),
                    ClientAppID = withdrawal.ClientAppID,
                    GatewayTransactionID = transferCode,
                    EventType = eventType,
                    RawPayload = rawBody,
                    Status = "PENDING",
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.WebhookEvents.Add(webhookEvent);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                _logger.LogInformation("Duplicate transfer webhook event {EventType} for {TransferCode}", eventType, transferCode);
            }

            await dbTransaction.CommitAsync(cancellationToken);

            var webhookEventId = await _dbContext.WebhookEvents
                .Where(w => w.GatewayTransactionID == transferCode && w.EventType == eventType)
                .Select(w => w.ID)
                .FirstOrDefaultAsync(cancellationToken);

            if (webhookEventId != Guid.Empty)
            {
                _backgroundJobClient.Enqueue<WebhookDeliveryService>(service => service.DeliverWebhookAsync(webhookEventId));
            }

            return Result.Success("Transfer webhook processed successfully");
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
