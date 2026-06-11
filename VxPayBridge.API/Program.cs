using Carter;
using FluentValidation;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using VxPayBridge.API.Database;
using VxPayBridge.API.Middlewares;
using VxPayBridge.API.SharedServices.Providers;
using VxPayBridge.API.SharedServices.Auth;
using VxPayBridge.API.SharedServices.Ledger;
using VxPayBridge.API.SharedServices.Sms;
using VxPayBridge.API.SharedServices.Webhooks;
using VxPayBridge.API.SharedServices.Withdrawals;

namespace VxPayBridge.API;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var assembly = typeof(Program).Assembly;

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        
        builder.Services.AddCarter();
        builder.Services.AddMediatR(config => config.RegisterServicesFromAssemblies(assembly));
        builder.Services.AddValidatorsFromAssembly(assembly);
        
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

        builder.Services.AddDbContext<DatabaseContext>(options =>
            options.UseNpgsql(connectionString)
                .ConfigureWarnings(warnings => warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
        );

        // Configure Hangfire
        builder.Services.AddHangfire(configuration => configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(options =>
                options.UseNpgsqlConnection(connectionString)));

        builder.Services.AddHangfireServer(options =>
        {
            options.Queues = new[] { "sms", "default" };
        });

        builder.Services.AddHttpClient();
        builder.Services.AddHttpClient<ISmsService, ArkeselSmsService>(client =>
        {
            client.BaseAddress = new Uri("https://sms.arkesel.com/");
        });
        builder.Services.AddHttpContextAccessor();

        // Register custom services
        builder.Services.AddScoped<IPaymentProvider, PaystackPaymentProvider>();
        builder.Services.AddScoped<UserTokenService>();
        builder.Services.AddScoped<LedgerService>();
        builder.Services.AddScoped<WebhookDeliveryService>();
        builder.Services.AddScoped<WithdrawalProcessingService>();

        var app = builder.Build();

        if (args.Contains("--migrate"))
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            db.Database.Migrate();
            Console.WriteLine("Migrations applied successfully.");
            return;
        }

        app.UseSwagger();
        app.UseSwaggerUI();

        app.UseUserAuthentication();
        app.UseClientAuthentication();

        app.MapCarter();
        app.UseHangfireDashboard("/hangfire");
        RecurringJob.AddOrUpdate<WebhookDeliveryService>(
            "deliver-pending-webhooks",
            service => service.EnqueuePendingWebhookDeliveriesAsync(),
            Cron.Minutely);
        RecurringJob.AddOrUpdate<WithdrawalProcessingService>(
            "process-queued-withdrawals",
            service => service.ProcessQueuedWithdrawalsAsync(),
            Cron.Minutely);

        app.Run();
    }
}
