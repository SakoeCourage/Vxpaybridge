using Carter;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VxPayBridge.API.Database;
using VxPayBridge.API.Database.Entities;
using VxPayBridge.API.SharedServices.Ledger;
using VxPayBridge.API.SharedServices.Providers;
using VxPayBridge.API.SharedServices.Withdrawals;

namespace VxPayBridge.API.Domain.Payments;

public class ConfirmWithdrawalRequest
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "GHS";
    public string AudType { get; set; } = string.Empty;
    public string AudId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string ClientReference { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public class ConfirmWithdrawalResponse
{
    public bool Success { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? TransferCode { get; set; }
    public string? Error { get; set; }
}

public class MapConfirmWithdrawalEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/payments/withdrawal/confirm",
                async (
                    [FromBody] ConfirmWithdrawalRequest body,
                    HttpContext context,
                    DatabaseContext dbContext,
                    LedgerService ledgerService,
                    IPaymentProvider paymentProvider,
                    IBackgroundJobClient backgroundJobClient) =>
                {
                    if (!TryGetClientAppId(context, out var clientAppId))
                    {
                        return Results.Unauthorized();
                    }

                    var validationError = Validate(body);
                    if (validationError != null)
                    {
                        return Results.BadRequest(new { error = validationError });
                    }

                    var existingWithdrawal = await dbContext.Withdrawals
                        .FirstOrDefaultAsync(w => w.ClientAppID == clientAppId && w.ClientReference == body.ClientReference);

                    if (existingWithdrawal != null)
                    {
                        if (existingWithdrawal.Status is "QUEUED" or "PROCESSING")
                        {
                            backgroundJobClient.Enqueue<WithdrawalProcessingService>(
                                service => service.ProcessWithdrawalAsync(existingWithdrawal.ID));
                        }

                        return Results.Ok(new ConfirmWithdrawalResponse
                        {
                            Success = existingWithdrawal.Status != "FAILED",
                            Status = existingWithdrawal.Status,
                            TransferCode = existingWithdrawal.TransferCode,
                            Error = existingWithdrawal.ErrorMessage
                        });
                    }

                    var provider = await FindProviderAsync(paymentProvider, body.Code);
                    if (provider == null)
                    {
                        return Results.UnprocessableEntity(new { error = "Invalid provider code" });
                    }

                    var account = await ledgerService.GetOrCreateAccountAsync(
                        clientAppId,
                        body.AudType,
                        body.AudId,
                        body.Currency);

                    var withdrawal = new Withdrawal
                    {
                        ID = Guid.NewGuid(),
                        ClientAppID = clientAppId,
                        LedgerAccountID = account.ID,
                        ClientReference = body.ClientReference,
                        AudType = body.AudType,
                        AudID = body.AudId,
                        Amount = body.Amount,
                        Currency = body.Currency,
                        ProviderCode = body.Code,
                        ProviderType = provider.Type,
                        AccountNumber = body.AccountNumber,
                        AccountName = body.AccountName,
                        Status = "QUEUED",
                        CreatedAt = DateTime.UtcNow
                    };

                    dbContext.Withdrawals.Add(withdrawal);
                    try
                    {
                        await dbContext.SaveChangesAsync();
                    }
                    catch (DbUpdateException)
                    {
                        dbContext.Entry(withdrawal).State = EntityState.Detached;
                        existingWithdrawal = await dbContext.Withdrawals
                            .FirstOrDefaultAsync(w => w.ClientAppID == clientAppId && w.ClientReference == body.ClientReference);

                        if (existingWithdrawal == null)
                        {
                            throw;
                        }

                        if (existingWithdrawal.Status is "QUEUED" or "PROCESSING")
                        {
                            backgroundJobClient.Enqueue<WithdrawalProcessingService>(
                                service => service.ProcessWithdrawalAsync(existingWithdrawal.ID));
                        }

                        return Results.Ok(new ConfirmWithdrawalResponse
                        {
                            Success = existingWithdrawal.Status != "FAILED",
                            Status = existingWithdrawal.Status,
                            TransferCode = existingWithdrawal.TransferCode,
                            Error = existingWithdrawal.ErrorMessage
                        });
                    }

                    var reserveResult = await ledgerService.ReserveWithdrawalAsync(withdrawal);
                    if (reserveResult.IsFailure)
                    {
                        withdrawal.Status = "FAILED";
                        withdrawal.ErrorMessage = reserveResult.Error.Message;
                        withdrawal.UpdatedAt = DateTime.UtcNow;
                        await dbContext.SaveChangesAsync();
                        return Results.UnprocessableEntity(new { error = reserveResult.Error.Message });
                    }

                    backgroundJobClient.Enqueue<WithdrawalProcessingService>(
                        service => service.ProcessWithdrawalAsync(withdrawal.ID));

                    return Results.Ok(new ConfirmWithdrawalResponse
                    {
                        Success = true,
                        Status = withdrawal.Status,
                        TransferCode = null,
                        Error = null
                    });
                })
            .WithTags("Payments")
            .WithName("ConfirmWithdrawal")
            .Produces<ConfirmWithdrawalResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status422UnprocessableEntity);
    }

    private static string? Validate(ConfirmWithdrawalRequest body)
    {
        if (body.Amount <= 0) return "Amount must be greater than zero";
        if (string.IsNullOrWhiteSpace(body.Currency)) return "Currency is required";
        if (string.IsNullOrWhiteSpace(body.AudType)) return "AudType is required";
        if (string.IsNullOrWhiteSpace(body.AudId)) return "AudId is required";
        if (string.IsNullOrWhiteSpace(body.Code)) return "Code is required";
        if (string.IsNullOrWhiteSpace(body.AccountNumber)) return "AccountNumber is required";
        if (string.IsNullOrWhiteSpace(body.AccountName)) return "AccountName is required";
        if (string.IsNullOrWhiteSpace(body.ClientReference)) return "ClientReference is required";
        return null;
    }

    private static async Task<PaymentProviderOption?> FindProviderAsync(IPaymentProvider paymentProvider, string code)
    {
        var banks = await paymentProvider.GetBanksAsync();
        if (banks.IsSuccess)
        {
            var bank = banks.Value!.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));
            if (bank != null) return bank;
        }

        var mobileMoneyProviders = await paymentProvider.GetMobileMoneyProvidersAsync();
        if (mobileMoneyProviders.IsSuccess)
        {
            return mobileMoneyProviders.Value!.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static bool TryGetClientAppId(HttpContext context, out Guid clientAppId)
    {
        clientAppId = default;
        var clientAppIdObj = context.Items["ClientAppId"];
        return clientAppIdObj != null && Guid.TryParse(clientAppIdObj.ToString(), out clientAppId);
    }
}
