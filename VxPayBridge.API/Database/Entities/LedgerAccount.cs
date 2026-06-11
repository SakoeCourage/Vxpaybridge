namespace VxPayBridge.API.Database.Entities;

public class LedgerAccount
{
    public Guid ID { get; set; }
    public Guid ClientAppID { get; set; }
    public string AudType { get; set; } = string.Empty;
    public string AudID { get; set; } = string.Empty;
    public string Currency { get; set; } = "GHS";
    public decimal CurrentBalance { get; set; }
    public decimal PendingBalance { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ClientApp? ClientApp { get; set; }
}
