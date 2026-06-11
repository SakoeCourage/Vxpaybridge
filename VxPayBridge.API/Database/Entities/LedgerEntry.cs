namespace VxPayBridge.API.Database.Entities;

public class LedgerEntry
{
    public Guid ID { get; set; }
    public Guid ClientAppID { get; set; }
    public Guid LedgerAccountID { get; set; }
    public Guid? PaymentTransactionID { get; set; }
    public Guid? WithdrawalID { get; set; }
    public string Type { get; set; } = string.Empty; // CREDIT, DEBIT
    public string Reason { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "GHS";
    public decimal BalanceAfter { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ClientApp? ClientApp { get; set; }
    public LedgerAccount? LedgerAccount { get; set; }
    public PaymentTransaction? PaymentTransaction { get; set; }
    public Withdrawal? Withdrawal { get; set; }
}
