namespace VxPayBridge.API.Database.Entities;

public class WebhookEvent
{
    public Guid ID { get; set; }
    public Guid ClientAppID { get; set; }
    public string GatewayTransactionID { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string RawPayload { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING"; // PENDING, DELIVERED, FAILED
    public int RetryCount { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ClientApp? ClientApp { get; set; }
}
