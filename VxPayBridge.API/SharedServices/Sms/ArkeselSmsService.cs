using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hangfire;

namespace VxPayBridge.API.SharedServices.Sms;

public class ArkeselSmsService : ISmsService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ArkeselSmsService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ArkeselSmsService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<ArkeselSmsService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendSmsAsync(string phoneNumber, string message)
    {
        var apiKey = _configuration["Sms:ArkeselApiKey"] ?? _configuration["SiteSettings:arkeselClientKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Arkesel API key is not configured. Set Sms:ArkeselApiKey.");
        }

        var payload = new
        {
            sender = _configuration["Sms:SenderName"] ?? "VXPayBridge",
            message,
            recipients = new[] { phoneNumber }
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v2/sms/send")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("api-key", apiKey);

        var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Arkesel SMS failed for {PhoneNumber}. Status: {Status}, Body: {Body}",
                phoneNumber, response.StatusCode, body);
            throw new HttpRequestException($"Arkesel SMS failed: {response.StatusCode} - {body}");
        }

        _logger.LogInformation("SMS sent to {PhoneNumber}", phoneNumber);
    }

    public string QueueSms(string phoneNumber, string message)
    {
        return BackgroundJob.Enqueue<ISmsService>("sms", service => service.SendSmsAsync(phoneNumber, message));
    }
}
