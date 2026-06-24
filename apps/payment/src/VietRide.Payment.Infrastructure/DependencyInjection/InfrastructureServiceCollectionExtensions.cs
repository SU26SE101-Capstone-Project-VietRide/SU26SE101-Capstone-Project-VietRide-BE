using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Refunds;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Events;
using VietRide.Payment.Application.Features.Wallets.BootstrapWallet;
using VietRide.Payment.Infrastructure.Http;
using VietRide.Payment.Infrastructure.Persistence.Repositories;
using VietRide.Payment.Infrastructure.Refunds;
using VietRide.Payment.Infrastructure.VnPay;
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
    public const string HangfireSchemaName = "hangfire";

    /// <summary>
    /// Adds Hangfire storage for Payment background jobs.
    /// </summary>
    public static IServiceCollection AddPaymentHangfire(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                connectionString,
                new PostgreSqlStorageOptions
                {
                    SchemaName = HangfireSchemaName,
                    PrepareSchemaIfNecessary = true,
                }));

        return services;
    }

    /// <summary>
    /// Adds Payment Infrastructure services to the DI container.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool registerConsumers = true)
    {
        services.AddScoped<IWalletRepository, WalletRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<ITopUpRequestRepository, TopUpRequestRepository>();
        services.AddScoped<IPlatformWalletRepository, PlatformWalletRepository>();
        services.AddScoped<IRefundFailureLogRepository, RefundFailureLogRepository>();
        services.AddScoped<IRefundRetryExecutor, DeferredRefundRetryExecutor>();
        services.Configure<VnPayOptions>(options =>
        {
            configuration.GetSection(VnPayOptions.SectionName).Bind(options);
            options.TmnCode = configuration["VNPAY_TMN_CODE"] ?? options.TmnCode;
            options.HashSecret = configuration["VNPAY_HASH_SECRET"] ?? options.HashSecret;
            options.BaseUrl = configuration["VNPAY_BASE_URL"] ?? options.BaseUrl;
            options.ReturnUrl = configuration["VNPAY_RETURN_URL"] ?? options.ReturnUrl;
            options.IpnUrl = configuration["VNPAY_IPN_URL"] ?? options.IpnUrl;

            if (long.TryParse(configuration["WALLET_TOP_UP_MIN_VND"], out var minimumTopUpAmount))
            {
                options.MinimumTopUpAmount = minimumTopUpAmount;
            }
        });
        services.AddScoped<IVnPayClient, VnPayClient>();

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
