using Microsoft.EntityFrameworkCore;
using VxPayBridge.API.Database.Entities;

namespace VxPayBridge.API.Database;

public class DatabaseContext : DbContext
{
    public DatabaseContext(DbContextOptions<DatabaseContext> options) : base(options)
    {
    }

    public DbSet<ClientApp> ClientApps { get; set; } = null!;
    public DbSet<WebhookEvent> WebhookEvents { get; set; } = null!;
    public DbSet<PaymentTransaction> PaymentTransactions { get; set; } = null!;
    public DbSet<LedgerAccount> LedgerAccounts { get; set; } = null!;
    public DbSet<LedgerEntry> LedgerEntries { get; set; } = null!;
    public DbSet<Withdrawal> Withdrawals { get; set; } = null!;
    public DbSet<AppUser> AppUsers { get; set; } = null!;
    public DbSet<UserOtp> UserOtps { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ClientApp>(entity =>
        {
            entity.HasKey(e => e.ID);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasIndex(e => e.ClientId).IsUnique();
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasKey(e => e.ID);
            entity.HasIndex(e => e.UserName).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.TelephoneNumber).IsUnique();

            entity.HasData(new AppUser
            {
                ID = new Guid("45db66dc-f9ef-47e3-bf08-751d946c07ab"),
                UserName = "Sakoe Courage",
                Email = "akorlicourage@gail.com",
                TelephoneNumber = "0203843143",
                PasswordHash = "40f66729a9551d5fedb4fff19d6416517cc49873e98b848c5b283fd6a38b9b52",
                IsActive = true,
                CreatedAt = new DateTime(2026, 6, 11, 0, 0, 0, DateTimeKind.Utc)
            });
        });

        modelBuilder.Entity<UserOtp>(entity =>
        {
            entity.HasKey(e => e.ID);
            entity.HasIndex(e => new { e.UserID, e.ExpiresAt });
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserID)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WebhookEvent>(entity =>
        {
            entity.HasKey(e => e.ID);
            entity.HasOne(e => e.ClientApp)
                .WithMany()
                .HasForeignKey(e => e.ClientAppID)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PaymentTransaction>(entity =>
        {
            entity.HasKey(e => e.ID);
            entity.HasIndex(e => new { e.ClientAppID, e.ClientReference }).IsUnique();
            entity.HasIndex(e => new { e.ClientAppID, e.AudType, e.AudID });
            entity.HasOne(e => e.ClientApp)
                .WithMany()
                .HasForeignKey(e => e.ClientAppID)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LedgerAccount>(entity =>
        {
            entity.HasKey(e => e.ID);
            entity.HasIndex(e => new { e.ClientAppID, e.AudType, e.AudID, e.Currency }).IsUnique();
            entity.HasOne(e => e.ClientApp)
                .WithMany()
                .HasForeignKey(e => e.ClientAppID)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LedgerEntry>(entity =>
        {
            entity.HasKey(e => e.ID);
            entity.HasIndex(e => e.PaymentTransactionID);
            entity.HasIndex(e => e.WithdrawalID);
            entity.HasIndex(e => new { e.ClientAppID, e.Reference }).IsUnique();
            entity.HasOne(e => e.ClientApp)
                .WithMany()
                .HasForeignKey(e => e.ClientAppID)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.LedgerAccount)
                .WithMany()
                .HasForeignKey(e => e.LedgerAccountID)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.PaymentTransaction)
                .WithMany()
                .HasForeignKey(e => e.PaymentTransactionID)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Withdrawal)
                .WithMany()
                .HasForeignKey(e => e.WithdrawalID)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Withdrawal>(entity =>
        {
            entity.HasKey(e => e.ID);
            entity.HasIndex(e => new { e.ClientAppID, e.ClientReference }).IsUnique();
            entity.HasIndex(e => e.TransferCode);
            entity.HasIndex(e => new { e.ClientAppID, e.AudType, e.AudID });
            entity.HasOne(e => e.ClientApp)
                .WithMany()
                .HasForeignKey(e => e.ClientAppID)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.LedgerAccount)
                .WithMany()
                .HasForeignKey(e => e.LedgerAccountID)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
