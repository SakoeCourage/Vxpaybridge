using Carter;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VxPayBridge.API.Database;

namespace VxPayBridge.API.Domain.Ledger;

public class MapLedgerEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/ledger/balance",
                async (
                    [FromQuery] string audType,
                    [FromQuery] string audId,
                    [FromQuery] string? currency,
                    HttpContext context,
                    DatabaseContext dbContext) =>
                {
                    if (!TryGetClientAppId(context, out var clientAppId))
                    {
                        return Results.Unauthorized();
                    }

                    if (string.IsNullOrWhiteSpace(audType) || string.IsNullOrWhiteSpace(audId))
                    {
                        return Results.BadRequest(new { error = "audType and audId are required" });
                    }

                    var requestedCurrency = string.IsNullOrWhiteSpace(currency) ? "GHS" : currency;
                    var account = await dbContext.LedgerAccounts.FirstOrDefaultAsync(
                        a => a.ClientAppID == clientAppId &&
                             a.AudType == audType &&
                             a.AudID == audId &&
                             a.Currency == requestedCurrency);

                    return Results.Ok(new
                    {
                        audType,
                        audId,
                        currency = requestedCurrency,
                        availableBalance = account?.CurrentBalance ?? 0,
                        pendingBalance = account?.PendingBalance ?? 0,
                        totalBalance = (account?.CurrentBalance ?? 0) + (account?.PendingBalance ?? 0)
                    });
                })
            .WithTags("Ledger")
            .WithName("GetLedgerBalance")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        app.MapGet("/api/ledger/transactions",
                async (
                    [FromQuery] string audType,
                    [FromQuery] string audId,
                    [FromQuery] string? currency,
                    [FromQuery] int? pageNumber,
                    [FromQuery] int? pageSize,
                    HttpContext context,
                    DatabaseContext dbContext) =>
                {
                    if (!TryGetClientAppId(context, out var clientAppId))
                    {
                        return Results.Unauthorized();
                    }

                    if (string.IsNullOrWhiteSpace(audType) || string.IsNullOrWhiteSpace(audId))
                    {
                        return Results.BadRequest(new { error = "audType and audId are required" });
                    }

                    var requestedCurrency = string.IsNullOrWhiteSpace(currency) ? "GHS" : currency;
                    var page = Math.Max(pageNumber ?? 1, 1);
                    var size = Math.Clamp(pageSize ?? 20, 1, 100);

                    var query = dbContext.LedgerEntries
                        .Include(e => e.LedgerAccount)
                        .Where(e => e.ClientAppID == clientAppId &&
                                    e.Currency == requestedCurrency &&
                                    e.LedgerAccount != null &&
                                    e.LedgerAccount.AudType == audType &&
                                    e.LedgerAccount.AudID == audId)
                        .OrderByDescending(e => e.CreatedAt);

                    var totalCount = await query.CountAsync();
                    var entries = await query
                        .Skip((page - 1) * size)
                        .Take(size)
                        .Select(e => new
                        {
                            id = e.ID,
                            type = e.Type,
                            reason = e.Reason,
                            reference = e.Reference,
                            amount = e.Amount,
                            currency = e.Currency,
                            balanceAfter = e.BalanceAfter,
                            paymentTransactionId = e.PaymentTransactionID,
                            withdrawalId = e.WithdrawalID,
                            createdAt = e.CreatedAt
                        })
                        .ToListAsync();

                    return Results.Ok(new
                    {
                        totalCount,
                        pageNumber = page,
                        pageSize = size,
                        data = entries
                    });
                })
            .WithTags("Ledger")
            .WithName("GetLedgerTransactions")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);
    }

    private static bool TryGetClientAppId(HttpContext context, out Guid clientAppId)
    {
        clientAppId = default;
        var clientAppIdObj = context.Items["ClientAppId"];
        return clientAppIdObj != null && Guid.TryParse(clientAppIdObj.ToString(), out clientAppId);
    }
}
