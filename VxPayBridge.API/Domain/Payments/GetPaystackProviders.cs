using Carter;
using Microsoft.AspNetCore.Mvc;
using VxPayBridge.API.SharedServices.Providers;

namespace VxPayBridge.API.Domain.Payments;

public class MapGetPaystackProvidersEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/payments/banks",
                async (IPaymentProvider paymentProvider) =>
                {
                    var result = await paymentProvider.GetBanksAsync();

                    if (result.IsFailure)
                    {
                        return Results.UnprocessableEntity(new { error = result.Error.Message });
                    }

                    var banks = result.Value!.Select(bank => new
                    {
                        name = bank.Name,
                        code = bank.Code
                    });

                    return Results.Ok(new { status = true, data = banks });
                })
            .WithTags("Payments")
            .WithName("GetBanks")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        app.MapGet("/api/payments/mobile-money-providers",
                async (IPaymentProvider paymentProvider) =>
                {
                    var result = await paymentProvider.GetMobileMoneyProvidersAsync();

                    if (result.IsFailure)
                    {
                        return Results.UnprocessableEntity(new { error = result.Error.Message });
                    }

                    var providers = result.Value!.Select(provider => new
                    {
                        name = provider.Name,
                        code = provider.Code
                    });

                    return Results.Ok(new { status = true, data = providers });
                })
            .WithTags("Payments")
            .WithName("GetMobileMoneyProviders")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        app.MapGet("/api/payments/resolve-account",
                async ([FromQuery] string accountNumber, [FromQuery] string code, IPaymentProvider paymentProvider) =>
                {
                    if (string.IsNullOrWhiteSpace(accountNumber) || string.IsNullOrWhiteSpace(code))
                    {
                        return Results.BadRequest(new { error = "accountNumber and code are required" });
                    }

                    var result = await paymentProvider.ResolveAccountAsync(accountNumber, code);

                    if (result.IsFailure)
                    {
                        return Results.UnprocessableEntity(new { error = result.Error.Message });
                    }

                    return Results.Ok(new ResolveAccountResponse
                    {
                        Status = true,
                        Data = new ResolveAccountData
                        {
                            AccountName = result.Value?.AccountName ?? string.Empty,
                            AccountNumber = result.Value?.AccountNumber ?? string.Empty
                        }
                    });
                })
            .WithTags("Payments")
            .WithName("ResolveAccount")
            .Produces<ResolveAccountResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status422UnprocessableEntity);
    }
}

public class ResolveAccountResponse
{
    public bool Status { get; set; }
    public ResolveAccountData Data { get; set; } = new();
}

public class ResolveAccountData
{
    public string AccountName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
}
