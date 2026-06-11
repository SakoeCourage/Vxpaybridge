using Microsoft.EntityFrameworkCore;
using Npgsql;
using VxPayBridge.API.Database;
using VxPayBridge.API.Database.Entities;
using VxPayBridge.API.SharedServices.Ledger;
using VxPayBridge.API.SharedServices.Providers;

namespace VxPayBridge.API.SharedServices.Withdrawals;

public class WithdrawalProcessingService
{
    private readonly DatabaseContext _dbContext;
    private readonly IPaymentProvider _paymentProvider;
    private readonly LedgerService _ledgerService;
    private readonly ILogger<WithdrawalProcessingService> _logger;

    public WithdrawalProcessingService(
        DatabaseContext dbContext,
        IPaymentProvider paymentProvider,
        LedgerService ledgerService,
        ILogger<WithdrawalProcessingService> logger)
    {
        _dbContext = dbContext;
        _paymentProvider = paymentProvider;
        _ledgerService = ledgerService;
        _logger = logger;
    }

    public async Task ProcessWithdrawalAsync(Guid withdrawalId)
    {
        var lockAcquired = await TryAcquireProcessingLockAsync(withdrawalId);
        if (!lockAcquired)
        {
            _logger.LogInformation("Withdrawal {WithdrawalId} is already being processed", withdrawalId);
            return;
        }

        try
        {
            await ProcessWithdrawalWithLockAsync(withdrawalId);
        }
        finally
        {
            await ReleaseProcessingLockAsync(withdrawalId);
        }
    }

    private async Task ProcessWithdrawalWithLockAsync(Guid withdrawalId)
    {
        var withdrawal = await _dbContext.Withdrawals.FirstOrDefaultAsync(w => w.ID == withdrawalId);
        if (withdrawal == null)
        {
            _logger.LogWarning("Withdrawal {WithdrawalId} not found for processing", withdrawalId);
            return;
        }

        if (IsTerminal(withdrawal.Status) || !string.IsNullOrWhiteSpace(withdrawal.TransferCode))
        {
            return;
        }

        if (withdrawal.Status != "QUEUED" && withdrawal.Status != "PROCESSING")
        {
            _logger.LogWarning("Withdrawal {WithdrawalId} is in unexpected state {Status}", withdrawalId, withdrawal.Status);
            return;
        }

        withdrawal.Status = "PROCESSING";
        withdrawal.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        var recipientResult = await _paymentProvider.CreateTransferRecipientAsync(
            withdrawal.ProviderType,
            withdrawal.AccountName,
            withdrawal.AccountNumber,
            withdrawal.ProviderCode,
            withdrawal.Currency);

        if (recipientResult.IsFailure)
        {
            await FailAndReverseAsync(withdrawal, recipientResult.Error.Message);
            return;
        }

        withdrawal.RecipientCode = recipientResult.Value!.RecipientCode;
        withdrawal.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        var transferResult = await _paymentProvider.InitiateTransferAsync(
            withdrawal.Amount,
            withdrawal.Currency,
            recipientResult.Value.RecipientCode,
            $"Withdrawal {withdrawal.ClientReference}",
            withdrawal.ClientReference);

        if (transferResult.IsFailure)
        {
            await FailAndReverseAsync(withdrawal, transferResult.Error.Message);
            return;
        }

        withdrawal.TransferCode = transferResult.Value!.TransferCode;
        withdrawal.Status = NormalizeTransferStatus(transferResult.Value.Status);
        withdrawal.UpdatedAt = DateTime.UtcNow;

        if (withdrawal.Status == "FAILED")
        {
            await _ledgerService.ReverseWithdrawalAsync(withdrawal, "WITHDRAWAL_FAILED");
        }
        else if (withdrawal.Status == "SUCCESS")
        {
            await _ledgerService.CompleteWithdrawalAsync(withdrawal);
        }

        await _dbContext.SaveChangesAsync();
    }

    private async Task<bool> TryAcquireProcessingLockAsync(Guid withdrawalId)
    {
        await _dbContext.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(
            "SELECT pg_try_advisory_lock(hashtext(@lockKey))",
            connection);
        command.Parameters.AddWithValue("lockKey", $"withdrawal:{withdrawalId}");

        var result = await command.ExecuteScalarAsync();
        return result is true;
    }

    private async Task ReleaseProcessingLockAsync(Guid withdrawalId)
    {
        try
        {
            var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
            await using var command = new NpgsqlCommand(
                "SELECT pg_advisory_unlock(hashtext(@lockKey))",
                connection);
            command.Parameters.AddWithValue("lockKey", $"withdrawal:{withdrawalId}");
            await command.ExecuteScalarAsync();
        }
        finally
        {
            await _dbContext.Database.CloseConnectionAsync();
        }
    }

    public async Task ProcessQueuedWithdrawalsAsync()
    {
        var withdrawalIds = await _dbContext.Withdrawals
            .Where(w => w.Status == "QUEUED" || (w.Status == "PROCESSING" && w.TransferCode == null))
            .OrderBy(w => w.CreatedAt)
            .Select(w => w.ID)
            .Take(50)
            .ToListAsync();

        foreach (var withdrawalId in withdrawalIds)
        {
            await ProcessWithdrawalAsync(withdrawalId);
        }
    }

    private async Task FailAndReverseAsync(Withdrawal withdrawal, string error)
    {
        withdrawal.Status = "FAILED";
        withdrawal.ErrorMessage = error;
        withdrawal.UpdatedAt = DateTime.UtcNow;
        await _ledgerService.ReverseWithdrawalAsync(withdrawal, "WITHDRAWAL_FAILED");
        await _dbContext.SaveChangesAsync();
    }

    private static bool IsTerminal(string status)
    {
        return status is "SUCCESS" or "FAILED" or "REVERSED";
    }

    private static string NormalizeTransferStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status)) return "PENDING";
        return status.ToUpperInvariant() switch
        {
            "SUCCESS" => "SUCCESS",
            "FAILED" => "FAILED",
            "REVERSED" => "REVERSED",
            _ => "PENDING"
        };
    }
}
