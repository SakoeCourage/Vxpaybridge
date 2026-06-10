using Carter;
using FluentValidation;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using VxPayBridge.API.Database;
using VxPayBridge.API.Middlewares;
using VxPayBridge.API.SharedServices.Providers;
using VxPayBridge.API.SharedServices.Webhooks;

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

        builder.Services.AddHangfireServer();

        builder.Services.AddHttpClient();
        builder.Services.AddHttpContextAccessor();

        // Register custom services
        builder.Services.AddScoped<IPaymentProvider, PaystackPaymentProvider>();
        builder.Services.AddScoped<WebhookDeliveryService>();

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

        app.UseInternalApiKeyAuthentication();
        app.UseClientAuthentication();

        app.MapCarter();
        app.UseHangfireDashboard("/hangfire");

        app.Run();
    }
}
