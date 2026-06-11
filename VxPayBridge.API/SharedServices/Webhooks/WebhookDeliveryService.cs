using System.Text;
using VxPayBridge.API.Database;
using VxPayBridge.API.SharedServices.Security;
using Microsoft.EntityFrameworkCore;
using Hangfire;
using Npgsql;

namespace VxPayBridge.API.SharedServices.Webhooks;

public class WebhookDeliveryService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WebhookDeliveryService> _logger;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public WebhookDeliveryService(
        IServiceProvider serviceProvider,
        ILogger<WebhookDeliveryService> logger,
        IBackgroundJobClient backgroundJobClient)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _backgroundJobClient = backgroundJobClient;
    }

    public async Task EnqueuePendingWebhookDeliveriesAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var webhookEventIds = await dbContext.WebhookEvents
            .Where(w => (w.Status == "PENDING" || w.Status == "FAILED") && w.RetryCount < 10)
            .OrderBy(w => w.CreatedAt)
            .Select(w => w.ID)
            .Take(100)
            .ToListAsync();

        foreach (var webhookEventId in webhookEventIds)
        {
            _backgroundJobClient.Enqueue<WebhookDeliveryService>(
                service => service.DeliverWebhookAsync(webhookEventId));
        }
    }

    public async Task DeliverWebhookAsync(Guid webhookEventId)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

        var lockAcquired = await TryAcquireDeliveryLockAsync(dbContext, webhookEventId);
        if (!lockAcquired)
        {
            _logger.LogInformation("Webhook event {Id} is already being delivered by another worker", webhookEventId);
            return;
        }

        try
        {
            var webhookEvent = await dbContext.WebhookEvents
                .Include(w => w.ClientApp)
                .FirstOrDefaultAsync(w => w.ID == webhookEventId);

            if (webhookEvent == null || webhookEvent.ClientApp == null)
            {
                _logger.LogError("Webhook event {Id} or ClientApp not found.", webhookEventId);
                return;
            }

            if (webhookEvent.Status == "DELIVERED")
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(webhookEvent.ClientApp.WebhookSigningSecret))
            {
                _logger.LogError("Webhook signing secret is missing for client app {ClientAppId}.", webhookEvent.ClientApp.ID);
                webhookEvent.Status = "FAILED";
                webhookEvent.UpdatedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync();
                return;
            }

            try
            {
                webhookEvent.RetryCount++;

                var client = httpClientFactory.CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Post, webhookEvent.ClientApp.WebhookUrl)
                {
                    Content = new StringContent(webhookEvent.RawPayload, Encoding.UTF8, "application/json")
                };

                var signature = HmacHelper.GenerateSha256Signature(
                    webhookEvent.ClientApp.WebhookSigningSecret,
                    webhookEvent.RawPayload);
                request.Headers.Add("x-payload-signature", signature);

                var response = await client.SendAsync(request);

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
        finally
        {
            await ReleaseDeliveryLockAsync(dbContext, webhookEventId);
        }
    }

    private static async Task<bool> TryAcquireDeliveryLockAsync(DatabaseContext dbContext, Guid webhookEventId)
    {
        await dbContext.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(
            "SELECT pg_try_advisory_lock(hashtext(@lockKey))",
            connection);
        command.Parameters.AddWithValue("lockKey", $"webhook-delivery:{webhookEventId}");

        var result = await command.ExecuteScalarAsync();
        return result is true;
    }

    private static async Task ReleaseDeliveryLockAsync(DatabaseContext dbContext, Guid webhookEventId)
    {
        try
        {
            var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
            await using var command = new NpgsqlCommand(
                "SELECT pg_advisory_unlock(hashtext(@lockKey))",
                connection);
            command.Parameters.AddWithValue("lockKey", $"webhook-delivery:{webhookEventId}");
            await command.ExecuteScalarAsync();
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }
}
