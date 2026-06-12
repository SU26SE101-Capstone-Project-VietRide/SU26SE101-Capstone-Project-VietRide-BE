using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Application.Features.Wallets.BootstrapWallet;
using VietRide.Payment.Infrastructure.Http;
using VietRide.Payment.Infrastructure.Persistence.Repositories;
using VietRide.Shared.Http.Handlers;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Messaging.DependencyInjection;

namespace VietRide.Payment.Infrastructure.DependencyInjection;

/// <summary>
/// Registers Payment Infrastructure services such as Redis (required by idempotency)
/// and outbound HTTP helpers.
/// </summary>
/// <remarks>
/// DB-CONTEXT GUARD: this method MUST NOT call AddVietRideDbContext / AddDbContext.
/// The PaymentDbContext is already registered at Program.cs via AddVietRideDbContext.
/// </remarks>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Adds Payment Infrastructure services to the DI container.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool registerConsumers = true)
    {
        services.AddScoped<IWalletRepository, WalletRepository>();

        if (registerConsumers)
        {
            services.AddVietRideEventConsumer<UserCreatedIntegrationEvent, BootstrapWalletCommandHandler>(options =>
            {
                options.QueueName = "payment.wallet-bootstrap";
                options.BindingKeys = [UserCreatedIntegrationEvent.EventType];
            });
        }

        // Redis — required by IdempotencyMiddleware (wired in Program.cs via AddVietRideIdempotency).
        // Falls back gracefully if REDIS_URL is absent (AbortOnConnectFail = false).
        var redisUrl = configuration["REDIS_URL"]
            ?? Environment.GetEnvironmentVariable("REDIS_URL")
            ?? "localhost:6379";
        var redisOptions = ConfigurationOptions.Parse(redisUrl);
        redisOptions.AbortOnConnectFail = false;
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisOptions));

        // Internal JWT provider — used by outbound delegating handlers.
        services.AddSingleton<IInternalJwtTokenProvider, InternalJwtTokenFactory>();
        services.AddHttpContextAccessor();
        services.AddTransient<InternalJwtDelegatingHandler>();
        services.AddTransient<CorrelationIdDelegatingHandler>();

        return services;
    }
}
