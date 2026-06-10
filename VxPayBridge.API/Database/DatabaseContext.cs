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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ClientApp>(entity =>
        {
            entity.HasKey(e => e.ID);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasIndex(e => e.ClientId).IsUnique();
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
            entity.HasIndex(e => e.ClientReference).IsUnique();
            entity.HasOne(e => e.ClientApp)
                .WithMany()
                .HasForeignKey(e => e.ClientAppID)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
