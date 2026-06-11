namespace VxPayBridge.API.Database.Entities;

public class UserOtp
{
    public Guid ID { get; set; }
    public Guid UserID { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UsedAt { get; set; }
    public int FailedAttempts { get; set; }

    public AppUser? User { get; set; }
}
