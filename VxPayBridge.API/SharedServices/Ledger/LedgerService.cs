using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using VxPayBridge.API.Database;
using VxPayBridge.API.Database.Entities;
using VxPayBridge.API.Shared;

namespace VxPayBridge.API.SharedServices.Ledger;

public class LedgerService
{
    private readonly DatabaseContext _dbContext;

    public LedgerService(DatabaseContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<LedgerAccount> GetOrCreateAccountAsync(
        Guid clientAppId,
        string audType,
        string audId,
        string currency,
        CancellationToken cancellationToken = default)
    {
        var account = await _dbContext.LedgerAccounts.FirstOrDefaultAsync(
            a => a.ClientAppID == clientAppId &&
                 a.AudType == audType &&
                 a.AudID == audId &&
                 a.Currency == currency,
            cancellationToken);

        if (account != null)
        {
            return account;
        }

        account = new LedgerAccount
        {
            ID = Guid.NewGuid(),
            ClientAppID = clientAppId,
            AudType = audType,
            AudID = audId,
            Currency = currency,
            CurrentBalance = 0,
            PendingBalance = 0,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.LedgerAccounts.Add(account);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _dbContext.Entry(account).State = EntityState.Detached;
            account = await _dbContext.LedgerAccounts.SingleAsync(
                a => a.ClientAppID == clientAppId &&
                     a.AudType == audType &&
                     a.AudID == audId &&
                     a.Currency == currency,
                cancellationToken);
        }

        return account;
    }

    public async Task CreditPaymentAsync(PaymentTransaction transaction, CancellationToken cancellationToken = default)
    {
        await using var dbTransaction = await BeginTransactionIfNeededAsync(cancellationToken);
        var reference = $"payment:{transaction.ID}";
        var exists = await _dbContext.LedgerEntries
            .AnyAsync(e => e.ClientAppID == transaction.ClientAppID && e.Reference == reference, cancellationToken);

        if (exists)
        {
            await CommitIfOwnedAsync(dbTransaction, cancellationToken);
            return;
        }

        var account = await GetOrCreateAccountAsync(
            transaction.ClientAppID,
            transaction.AudType,
            transaction.AudID,
            transaction.Currency,
            cancellationToken);

        account = await LockAccountAsync(account.ID, cancellationToken);

        account.CurrentBalance += transaction.Amount;
        account.UpdatedAt = DateTime.UtcNow;

        _dbContext.LedgerEntries.Add(new LedgerEntry
        {
            ID = Guid.NewGuid(),
            ClientAppID = transaction.ClientAppID,
            LedgerAccountID = account.ID,
            PaymentTransactionID = transaction.ID,
            Type = "CREDIT",
            Reason = "PAYMENT_SUCCESS",
            Reference = reference,
            Amount = transaction.Amount,
            Currency = transaction.Currency,
            BalanceAfter = account.CurrentBalance,
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await CommitIfOwnedAsync(dbTransaction, cancellationToken);
    }

    public async Task<Result> ReserveWithdrawalAsync(Withdrawal withdrawal, CancellationToken cancellationToken = default)
    {
        await using var dbTransaction = await BeginTransactionIfNeededAsync(cancellationToken);
        var reference = $"withdrawal:{withdrawal.ID}";
        var exists = await _dbContext.LedgerEntries
            .AnyAsync(e => e.ClientAppID == withdrawal.ClientAppID && e.Reference == reference, cancellationToken);

        if (exists)
        {
            await CommitIfOwnedAsync(dbTransaction, cancellationToken);
            return Result.Success();
        }

        var account = await LockAccountAsync(withdrawal.LedgerAccountID, cancellationToken);

        if (account.CurrentBalance < withdrawal.Amount)
        {
            await CommitIfOwnedAsync(dbTransaction, cancellationToken);
            return Result.Failure(Error.BadRequest("Insufficient available balance"));
        }

        account.CurrentBalance -= withdrawal.Amount;
        account.PendingBalance += withdrawal.Amount;
        account.UpdatedAt = DateTime.UtcNow;

        _dbContext.LedgerEntries.Add(new LedgerEntry
        {
            ID = Guid.NewGuid(),
            ClientAppID = withdrawal.ClientAppID,
            LedgerAccountID = account.ID,
            WithdrawalID = withdrawal.ID,
            Type = "DEBIT",
            Reason = "WITHDRAWAL_PENDING",
            Reference = reference,
            Amount = withdrawal.Amount,
            Currency = withdrawal.Currency,
            BalanceAfter = account.CurrentBalance,
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await CommitIfOwnedAsync(dbTransaction, cancellationToken);
        return Result.Success();
    }

    public async Task ReverseWithdrawalAsync(Withdrawal withdrawal, string reason, CancellationToken cancellationToken = default)
    {
        await using var dbTransaction = await BeginTransactionIfNeededAsync(cancellationToken);
        var reference = $"withdrawal-reversal:{withdrawal.ID}";
        var exists = await _dbContext.LedgerEntries
            .AnyAsync(e => e.ClientAppID == withdrawal.ClientAppID && e.Reference == reference, cancellationToken);

        if (exists)
        {
            await CommitIfOwnedAsync(dbTransaction, cancellationToken);
            return;
        }

        var account = await LockAccountAsync(withdrawal.LedgerAccountID, cancellationToken);

        if (account.PendingBalance < withdrawal.Amount)
        {
            throw new InvalidOperationException(
                $"Ledger invariant violation: pending balance {account.PendingBalance} is less than withdrawal amount {withdrawal.Amount} for account {account.ID}.");
        }

        account.CurrentBalance += withdrawal.Amount;
        account.PendingBalance -= withdrawal.Amount;
        account.UpdatedAt = DateTime.UtcNow;

        _dbContext.LedgerEntries.Add(new LedgerEntry
        {
            ID = Guid.NewGuid(),
            ClientAppID = withdrawal.ClientAppID,
            LedgerAccountID = account.ID,
            WithdrawalID = withdrawal.ID,
            Type = "CREDIT",
            Reason = reason,
            Reference = reference,
            Amount = withdrawal.Amount,
            Currency = withdrawal.Currency,
            BalanceAfter = account.CurrentBalance,
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await CommitIfOwnedAsync(dbTransaction, cancellationToken);
    }

    public async Task CompleteWithdrawalAsync(Withdrawal withdrawal, CancellationToken cancellationToken = default)
    {
        await using var dbTransaction = await BeginTransactionIfNeededAsync(cancellationToken);
        var reference = $"withdrawal-complete:{withdrawal.ID}";
        var exists = await _dbContext.LedgerEntries
            .AnyAsync(e => e.ClientAppID == withdrawal.ClientAppID && e.Reference == reference, cancellationToken);

        if (exists)
        {
            await CommitIfOwnedAsync(dbTransaction, cancellationToken);
            return;
        }

        var account = await LockAccountAsync(withdrawal.LedgerAccountID, cancellationToken);

        if (account.PendingBalance < withdrawal.Amount)
        {
            throw new InvalidOperationException(
                $"Ledger invariant violation: pending balance {account.PendingBalance} is less than withdrawal amount {withdrawal.Amount} for account {account.ID}.");
        }

        account.PendingBalance -= withdrawal.Amount;
        account.UpdatedAt = DateTime.UtcNow;

        _dbContext.LedgerEntries.Add(new LedgerEntry
        {
            ID = Guid.NewGuid(),
            ClientAppID = withdrawal.ClientAppID,
            LedgerAccountID = account.ID,
            WithdrawalID = withdrawal.ID,
            Type = "INFO",
            Reason = "WITHDRAWAL_SUCCESS",
            Reference = reference,
            Amount = withdrawal.Amount,
            Currency = withdrawal.Currency,
            BalanceAfter = account.CurrentBalance,
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await CommitIfOwnedAsync(dbTransaction, cancellationToken);
    }

    public async Task ReverseCompletedWithdrawalAsync(Withdrawal withdrawal, CancellationToken cancellationToken = default)
    {
        await using var dbTransaction = await BeginTransactionIfNeededAsync(cancellationToken);
        var reference = $"withdrawal-completed-reversal:{withdrawal.ID}";
        var exists = await _dbContext.LedgerEntries
            .AnyAsync(e => e.ClientAppID == withdrawal.ClientAppID && e.Reference == reference, cancellationToken);

        if (exists)
        {
            await CommitIfOwnedAsync(dbTransaction, cancellationToken);
            return;
        }

        var account = await LockAccountAsync(withdrawal.LedgerAccountID, cancellationToken);

        account.CurrentBalance += withdrawal.Amount;
        account.UpdatedAt = DateTime.UtcNow;

        _dbContext.LedgerEntries.Add(new LedgerEntry
        {
            ID = Guid.NewGuid(),
            ClientAppID = withdrawal.ClientAppID,
            LedgerAccountID = account.ID,
            WithdrawalID = withdrawal.ID,
            Type = "CREDIT",
            Reason = "WITHDRAWAL_REVERSED",
            Reference = reference,
            Amount = withdrawal.Amount,
            Currency = withdrawal.Currency,
            BalanceAfter = account.CurrentBalance,
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await CommitIfOwnedAsync(dbTransaction, cancellationToken);
    }

    private async Task<LedgerAccount> LockAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return await _dbContext.LedgerAccounts
            .FromSqlInterpolated($"SELECT * FROM \"LedgerAccounts\" WHERE \"ID\" = {accountId} FOR UPDATE")
            .SingleAsync(cancellationToken);
    }

    private async Task<IDbContextTransaction?> BeginTransactionIfNeededAsync(CancellationToken cancellationToken)
    {
        if (_dbContext.Database.CurrentTransaction != null)
        {
            return null;
        }

        return await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
    }

    private static async Task CommitIfOwnedAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken)
    {
        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }
}
