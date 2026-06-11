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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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

            var paystackResponse = JsonSerializer.Deserialize<PaystackInitializeResponse>(responseString, JsonOptions);
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

    public Task<Result<IReadOnlyList<PaymentProviderOption>>> GetBanksAsync()
    {
        return GetProviderOptionsAsync("bank?currency=GHS&country=ghana&type=ghipss&perPage=100", "banks");
    }

    public Task<Result<IReadOnlyList<PaymentProviderOption>>> GetMobileMoneyProvidersAsync()
    {
        return GetProviderOptionsAsync("bank?currency=GHS&country=ghana&type=mobile_money&perPage=100", "mobile money providers");
    }

    public async Task<Result<ResolvedPaymentAccount>> ResolveAccountAsync(string accountNumber, string code)
    {
        try
        {
            var endpoint = $"bank/resolve?account_number={Uri.EscapeDataString(accountNumber)}&bank_code={Uri.EscapeDataString(code)}";
            var response = await _httpClient.GetAsync(endpoint);
            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Paystack account resolve failed: {Response}", responseString);
                return Result.Failure<ResolvedPaymentAccount>(
                    new Error("Paystack.Error", "Failed to resolve Paystack account"));
            }

            var paystackResponse = JsonSerializer.Deserialize<PaystackResolveAccountResponse>(responseString, JsonOptions);
            if (paystackResponse == null || !paystackResponse.Status || paystackResponse.Data == null)
            {
                return Result.Failure<ResolvedPaymentAccount>(
                    new Error("Paystack.Error", paystackResponse?.Message ?? "Failed to resolve Paystack account"));
            }

            return Result.Success(new ResolvedPaymentAccount
            {
                AccountName = paystackResponse.Data.AccountName,
                AccountNumber = paystackResponse.Data.AccountNumber
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while resolving Paystack account {AccountNumber}", accountNumber);
            return Result.Failure<ResolvedPaymentAccount>(
                new Error("Paystack.Exception", "An error occurred resolving Paystack account"));
        }
    }

    private async Task<Result<IReadOnlyList<PaymentProviderOption>>> GetProviderOptionsAsync(string endpoint, string providerType)
    {
        try
        {
            var response = await _httpClient.GetAsync(endpoint);
            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Paystack {ProviderType} fetch failed: {Response}", providerType, responseString);
                return Result.Failure<IReadOnlyList<PaymentProviderOption>>(
                    new Error("Paystack.Error", $"Failed to fetch Paystack {providerType}"));
            }

            var paystackResponse = JsonSerializer.Deserialize<PaystackProviderListResponse>(responseString, JsonOptions);
            if (paystackResponse == null || !paystackResponse.Status)
            {
                return Result.Failure<IReadOnlyList<PaymentProviderOption>>(
                    new Error("Paystack.Error", paystackResponse?.Message ?? $"Failed to fetch Paystack {providerType}"));
            }

            var providers = paystackResponse.Data
                .Select(provider => new PaymentProviderOption
                {
                    Name = provider.Name,
                    Code = provider.Code
                })
                .ToList();

            return Result.Success<IReadOnlyList<PaymentProviderOption>>(providers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while fetching Paystack {ProviderType}", providerType);
            return Result.Failure<IReadOnlyList<PaymentProviderOption>>(
                new Error("Paystack.Exception", $"An error occurred fetching Paystack {providerType}"));
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

    private class PaystackProviderListResponse
    {
        [JsonPropertyName("status")]
        public bool Status { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public List<PaystackProviderEntry> Data { get; set; } = new();
    }

    private class PaystackProviderEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;
    }

    private class PaystackResolveAccountResponse
    {
        [JsonPropertyName("status")]
        public bool Status { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public PaystackResolveAccountData? Data { get; set; }
    }

    private class PaystackResolveAccountData
    {
        [JsonPropertyName("account_name")]
        public string AccountName { get; set; } = string.Empty;

        [JsonPropertyName("account_number")]
        public string AccountNumber { get; set; } = string.Empty;
    }
}
