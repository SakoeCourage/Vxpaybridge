using VxPayBridge.API.Database.Entities;
using VxPayBridge.API.Shared;

namespace VxPayBridge.API.SharedServices.Providers;

public interface IPaymentProvider
{
    Task<Result<PaymentInitializationResponse>> InitializePaymentAsync(PaymentTransaction transaction, string clientEmail, string callbackUrl);
}

public class PaymentInitializationResponse
{
    public string AuthorizationUrl { get; set; } = string.Empty;
    public string AccessCode { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}
