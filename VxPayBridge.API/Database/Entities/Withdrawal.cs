namespace VxPayBridge.API.Database.Entities;

public class Withdrawal
{
    public Guid ID { get; set; }
    public Guid ClientAppID { get; set; }
    public Guid LedgerAccountID { get; set; }
    public string ClientReference { get; set; } = string.Empty;
    public string AudType { get; set; } = string.Empty;
    public string AudID { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "GHS";
    public string ProviderCode { get; set; } = string.Empty;
    public string ProviderType { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string? RecipientCode { get; set; }
    public string? TransferCode { get; set; }
    public string Status { get; set; } = "PENDING"; // PENDING, SUCCESS, FAILED, REVERSED
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ClientApp? ClientApp { get; set; }
    public LedgerAccount? LedgerAccount { get; set; }
}
