using VxPayBridge.API.Database.Entities;
using VxPayBridge.API.Shared;

namespace VxPayBridge.API.SharedServices.Providers;

public interface IPaymentProvider
{
    Task<Result<PaymentInitializationResponse>> InitializePaymentAsync(PaymentTransaction transaction, string clientEmail, string callbackUrl);
    Task<Result<IReadOnlyList<PaymentProviderOption>>> GetBanksAsync();
    Task<Result<IReadOnlyList<PaymentProviderOption>>> GetMobileMoneyProvidersAsync();
    Task<Result<ResolvedPaymentAccount>> ResolveAccountAsync(string accountNumber, string code);
    Task<Result<TransferRecipientResponse>> CreateTransferRecipientAsync(string type, string name, string accountNumber, string code, string currency);
    Task<Result<TransferInitiationResponse>> InitiateTransferAsync(decimal amount, string currency, string recipientCode, string reason, string reference);
}

public class PaymentInitializationResponse
{
    public string AuthorizationUrl { get; set; } = string.Empty;
    public string AccessCode { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

public class PaymentProviderOption
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

public class ResolvedPaymentAccount
{
    public string AccountName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
}

public class TransferRecipientResponse
{
    public string RecipientCode { get; set; } = string.Empty;
}

public class TransferInitiationResponse
{
    public string TransferCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
