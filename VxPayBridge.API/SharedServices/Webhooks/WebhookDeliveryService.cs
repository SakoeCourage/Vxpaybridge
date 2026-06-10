using System.Text;
using VxPayBridge.API.Database;
using VxPayBridge.API.SharedServices.Security;
using Microsoft.EntityFrameworkCore;

namespace VxPayBridge.API.SharedServices.Webhooks;

public class WebhookDeliveryService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WebhookDeliveryService> _logger;

    public WebhookDeliveryService(IServiceProvider serviceProvider, ILogger<WebhookDeliveryService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task DeliverWebhookAsync(Guid webhookEventId)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

        var webhookEvent = await dbContext.WebhookEvents
            .Include(w => w.ClientApp)
            .FirstOrDefaultAsync(w => w.ID == webhookEventId);

        if (webhookEvent == null || webhookEvent.ClientApp == null)
        {
            _logger.LogError("Webhook event {Id} or ClientApp not found.", webhookEventId);
            return;
        }

        try
        {
            webhookEvent.RetryCount++;

            var client = httpClientFactory.CreateClient();
            var content = new StringContent(webhookEvent.RawPayload, Encoding.UTF8, "application/json");

            // Sign the payload using the client's secret so the client can verify it came from VxPayBridge
            var signature = HmacHelper.GenerateSignature(webhookEvent.ClientApp.ClientSecretHash, webhookEvent.RawPayload);
            content.Headers.Add("x-vx-signature", signature);

            var response = await client.PostAsync(webhookEvent.ClientApp.WebhookUrl, content);

            if (response.IsSuccessStatusCode)
            {
                webhookEvent.Status = "DELIVERED";
                webhookEvent.UpdatedAt = DateTime.UtcNow;
                _logger.LogInformation("Successfully delivered webhook {Id} to {Url}", webhookEventId, webhookEvent.ClientApp.WebhookUrl);
            }
            else
            {
                webhookEvent.Status = "FAILED";
                webhookEvent.UpdatedAt = DateTime.UtcNow;
                _logger.LogWarning("Failed to deliver webhook {Id}. Status Code: {StatusCode}", webhookEventId, response.StatusCode);
                
                // If we want to retry using Hangfire, we throw an exception so Hangfire automatically retries the job
                throw new Exception($"Webhook delivery failed with status code {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            webhookEvent.Status = "FAILED";
            webhookEvent.UpdatedAt = DateTime.UtcNow;
            _logger.LogError(ex, "Exception while delivering webhook {Id}", webhookEventId);
            
            await dbContext.SaveChangesAsync();
            throw; // Re-throw to trigger Hangfire retry
        }

        await dbContext.SaveChangesAsync();
    }
}
