using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VxPayBridge.API.Database.Entities;
using VxPayBridge.API.Shared;

namespace VxPayBridge.API.SharedServices.Providers;

public class PaystackPaymentProvider : IPaymentProvider
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PaystackPaymentProvider> _logger;

    public PaystackPaymentProvider(HttpClient httpClient, IConfiguration configuration, ILogger<PaystackPaymentProvider> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;

        var secretKey = _configuration["Paystack:SecretKey"];
        _httpClient.BaseAddress = new Uri("https://api.paystack.co/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<Result<PaymentInitializationResponse>> InitializePaymentAsync(PaymentTransaction transaction, string clientEmail, string callbackUrl)
    {
        try
        {
            // Paystack expects amount in kobo/pesewas (base currency unit)
            var amountInBaseUnit = (long)(transaction.Amount * 100);

            var request = new
            {
                amount = amountInBaseUnit,
                email = clientEmail,
                reference = transaction.GatewayTransactionID,
                currency = transaction.Currency,
                callback_url = callbackUrl,
                metadata = new
                {
                    gateway_client_code = transaction.ClientApp?.Code,
                    gateway_transaction_id = transaction.GatewayTransactionID,
                    client_reference = transaction.ClientReference
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("transaction/initialize", content);

            var responseString = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Paystack initialization failed: {Response}", responseString);
                return Result.Failure<PaymentInitializationResponse>(new Error("Paystack.Error", "Failed to initialize payment with Paystack"));
            }

            var paystackResponse = JsonSerializer.Deserialize<PaystackInitializeResponse>(responseString);
            if (paystackResponse == null || !paystackResponse.Status)
            {
                return Result.Failure<PaymentInitializationResponse>(new Error("Paystack.Error", paystackResponse?.Message ?? "Unknown Paystack error"));
            }

            return Result.Success(new PaymentInitializationResponse
            {
                AuthorizationUrl = paystackResponse.Data.AuthorizationUrl,
                AccessCode = paystackResponse.Data.AccessCode,
                Reference = paystackResponse.Data.Reference
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while initializing Paystack payment");
            return Result.Failure<PaymentInitializationResponse>(new Error("Paystack.Exception", "An error occurred communicating with Paystack"));
        }
    }

    private class PaystackInitializeResponse
    {
        [JsonPropertyName("status")]
        public bool Status { get; set; }
        
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
        
        [JsonPropertyName("data")]
        public PaystackInitializeData Data { get; set; } = new();
    }

    private class PaystackInitializeData
    {
        [JsonPropertyName("authorization_url")]
        public string AuthorizationUrl { get; set; } = string.Empty;
        
        [JsonPropertyName("access_code")]
        public string AccessCode { get; set; } = string.Empty;
        
        [JsonPropertyName("reference")]
        public string Reference { get; set; } = string.Empty;
    }
}
