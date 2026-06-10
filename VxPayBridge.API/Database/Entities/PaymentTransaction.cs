namespace VxPayBridge.API.Database.Entities;

public class PaymentTransaction
{
    public Guid ID { get; set; }
    public Guid ClientAppID { get; set; }
    public string ClientReference { get; set; } = string.Empty;
    public string GatewayTransactionID { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING"; // INITIALIZING, PENDING, SUCCESS, FAILED
    public string? AuthorizationUrl { get; set; }
    public string? AccessCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ClientApp? ClientApp { get; set; }
}
