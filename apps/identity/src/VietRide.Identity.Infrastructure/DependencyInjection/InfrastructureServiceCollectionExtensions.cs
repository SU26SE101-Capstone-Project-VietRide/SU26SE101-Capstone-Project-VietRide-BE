using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Http;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Infrastructure.ExternalClients;
using VietRide.Identity.Infrastructure.Http;
using VietRide.Identity.Infrastructure.Jobs;
using VietRide.Identity.Infrastructure.Messaging;
using VietRide.Identity.Infrastructure.Persistence.Repositories;
using VietRide.Identity.Infrastructure.Security;
using VietRide.Identity.Infrastructure.Seed;
using VietRide.Shared.Http.Handlers;
using VietRide.Shared.Http.Resilience;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Messaging.DependencyInjection;

namespace VietRide.Identity.Infrastructure.DependencyInjection;

/// <summary>
/// Registers all Infrastructure-layer services: repositories, security services,
/// and external-client stubs.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public const string HangfireSchemaName = "hangfire";

    public static IServiceCollection AddIdentityHangfire(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(connectionString, new PostgreSqlStorageOptions
            {
                SchemaName = HangfireSchemaName,
                PrepareSchemaIfNecessary = true,
            }));
        return services;
    }

    /// <summary>
    /// Adds Identity Infrastructure services (repositories + security + email stub) to
    /// the DI container.  Call after <c>AddVietRideDbContext&lt;IdentityDbContext&gt;</c>
    /// and after binding <c>IdentityJwt</c> configuration.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        bool registerEventConsumer = true)
    {
        // ------------------------------------------------------------------
        // Configuration
        // ------------------------------------------------------------------
        services.Configure<JwtSigningOptions>(options =>
        {
            configuration.GetSection(JwtSigningOptions.SectionName).Bind(options);

            // Production SOT names are plain environment variables. Keep the
            // IdentityJwt section for local/dev config, but let explicit env vars
            // override it when present in production/container environments.
            var privateKey = configuration["USER_JWT_PRIVATE_KEY"];
            if (!string.IsNullOrWhiteSpace(privateKey))
                options.PrivateKey = privateKey;

            var kid = configuration["USER_JWT_KID"];
            if (!string.IsNullOrWhiteSpace(kid))
                options.Kid = kid;
        });

        services.Configure<GoogleOAuthOptions>(options =>
        {
            configuration.GetSection(GoogleOAuthOptions.SectionName).Bind(options);

            var clientId = configuration["GOOGLE_OAUTH_CLIENT_ID"];
            if (!string.IsNullOrWhiteSpace(clientId))
                options.ClientId = clientId;

            var clientSecret = configuration["GOOGLE_OAUTH_CLIENT_SECRET"];
            if (!string.IsNullOrWhiteSpace(clientSecret))
                options.ClientSecret = clientSecret;
        });

        // ------------------------------------------------------------------
        // Repositories
        // ------------------------------------------------------------------
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IEmailVerificationTokenRepository, EmailVerificationTokenRepository>();
        services.AddScoped<IUserDeviceRepository, UserDeviceRepository>();
        services.AddScoped<IActivityLogRepository, ActivityLogRepository>();
        services.AddScoped<IOAuthIdentityRepository, OAuthIdentityRepository>();
        services.AddScoped<IOperatorRepository, OperatorRepository>();
        services.AddScoped<IOperatorSubscriptionRepository, OperatorSubscriptionRepository>();
        services.AddScoped<ISubscriptionPlanRepository, SubscriptionPlanRepository>();
        services.AddScoped<ISubscriptionUpgradeAttemptRepository, SubscriptionUpgradeAttemptRepository>();
        services.AddScoped<ISubscriptionQuotaAllocationRepository, SubscriptionQuotaAllocationRepository>();

        AddSubscriptionPaymentClient(services, configuration);
        services.AddScoped<SubscriptionLifecycleJob>();
        if (registerEventConsumer)
        {
            services.AddVietRideEventConsumer<SubscriptionPaymentSucceededIntegrationEvent, SubscriptionPaymentSucceededIntegrationEventHandler>(options =>
            {
                options.QueueName = "identity.subscription-payment-succeeded";
                options.BindingKeys = ["payment.subscription.payment_succeeded"];
            });
        }

        // ------------------------------------------------------------------
        // Startup seeders
        // ------------------------------------------------------------------
        services.AddScoped<ISystemAdminBootstrapStore, EfSystemAdminBootstrapStore>();
        services.AddScoped<BootstrapAdminSeeder>();

        // ------------------------------------------------------------------
        // Security services
        // ------------------------------------------------------------------
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<IInitialPasswordTokenService, InitialPasswordTokenService>();
        services.AddSingleton<IAccessTokenService, RsaAccessTokenService>();
        services.AddSingleton<IJwksProvider, JwksProvider>();
        services.AddSingleton<IGoogleIdTokenVerifier, GoogleIdTokenVerifier>();
        services.AddScoped<IRefreshTokenFactory, RefreshTokenFactory>();
        services.AddSingleton<IOtpFailedAttemptPersister, OtpFailedAttemptPersister>();
        services.AddSingleton<IFailedLoginPersister, FailedLoginPersister>();
        services.AddSingleton<IRefreshTokenFamilyRevoker, RefreshTokenFamilyRevoker>();
        services.AddSingleton<ILoginLockoutCounter, RedisLoginLockoutCounter>();

        // ------------------------------------------------------------------
        // Email delivery — provider switch (EMAIL_PROVIDER)
        // ------------------------------------------------------------------
        // SENDGRID = real delivery via the Notification Service internal HTTP
        //            endpoint (POST /internal/v1/emails → SendGrid). The
        //            container/prod default (set in docker-compose).
        // LOG      = LoggingEmailService (logs only) — the default when the
        //            switch is absent, so local dev works without Notification
        //            running and DI resolves without INTERNAL_JWT_SECRET.
        AddEmailDelivery(services, configuration);

        // ------------------------------------------------------------------
        // Redis — OTP rate-limit (BSOT §6.9). Falls back gracefully if not configured.
        // ------------------------------------------------------------------
        var redisUrl = configuration["REDIS_URL"] ?? "localhost:6379";
        var redisOptions = ConfigurationOptions.Parse(redisUrl);
        redisOptions.AbortOnConnectFail = false;
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisOptions));
        services.AddSingleton<IOtpRateLimiter, RedisOtpRateLimiter>();
        services.AddSingleton<IPasswordResetRateLimiter, RedisPasswordResetRateLimiter>();

        return services;
    }

    /// <summary>
    /// Wires the <see cref="IEmailService"/> implementation selected by the
    /// <c>EMAIL_PROVIDER</c> switch (default <c>LOG</c>). <c>SENDGRID</c> binds
    /// <see cref="NotificationEmailService"/> on top of the typed
    /// <see cref="INotificationEmailClient"/> with the standard Internal-JWT +
    /// correlation-id + Polly pipeline (VietRide.Shared.Http).
    /// </summary>
    private static void AddEmailDelivery(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = (configuration["EMAIL_PROVIDER"]
            ?? Environment.GetEnvironmentVariable("EMAIL_PROVIDER")
            ?? "LOG").Trim();

        if (string.Equals(provider, "LOG", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEmailService, LoggingEmailService>();
            return;
        }

        if (!string.Equals(provider, "SENDGRID", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "EMAIL_PROVIDER must be either 'LOG' or 'SENDGRID'.");
        }

        // Internal JWT signer + delegating handlers shared with the other
        // .NET services (do not hand-roll a second signer per BSOT §5.3).
        services.TryAddSingleton<IInternalJwtTokenProvider, InternalJwtTokenFactory>();
        services.AddHttpContextAccessor();
        services.TryAddTransient<InternalJwtDelegatingHandler>();
        services.TryAddTransient<CorrelationIdDelegatingHandler>();

        services
            .AddHttpClient<INotificationEmailClient, NotificationEmailClient>(client =>
            {
                var baseUrl = ResolveNotificationBaseUrl(configuration);
                client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(
                    new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
            .AddHttpMessageHandler<InternalJwtDelegatingHandler>()
            .AddPolicyHandler(HttpResiliencePolicies.GetRetryPolicy())
            .AddPolicyHandler(HttpResiliencePolicies.GetCircuitBreakerPolicy());

        services.AddScoped<IEmailService, NotificationEmailService>();
    }

    /// <summary>
    /// Resolves the Notification Service base URL for the email endpoint from
    /// <c>EMAIL_SERVICE_BASE_URL</c> (BSOT §3.5), defaulting to the compose
    /// hostname <c>http://notification:3002</c> (BSOT line 2364).
    /// </summary>
    private static string ResolveNotificationBaseUrl(IConfiguration configuration)
    {
        return configuration["EMAIL_SERVICE_BASE_URL"]
            ?? Environment.GetEnvironmentVariable("EMAIL_SERVICE_BASE_URL")
            ?? "http://notification:3002";
    }

    private static void AddSubscriptionPaymentClient(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddSingleton<IInternalJwtTokenProvider, InternalJwtTokenFactory>();
        services.AddHttpContextAccessor();
        services.TryAddTransient<InternalJwtDelegatingHandler>();
        services.TryAddTransient<CorrelationIdDelegatingHandler>();

        services.AddHttpClient<ISubscriptionPaymentClient, SubscriptionPaymentClient>(client =>
            {
                var baseUrl = configuration["PAYMENT_SERVICE_BASE_URL"]
                    ?? Environment.GetEnvironmentVariable("PAYMENT_SERVICE_BASE_URL")
                    ?? "http://payment:8080";
                client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
            .AddHttpMessageHandler<InternalJwtDelegatingHandler>()
            .AddPolicyHandler(HttpResiliencePolicies.GetRetryPolicy())
            .AddPolicyHandler(HttpResiliencePolicies.GetCircuitBreakerPolicy());
    }
}
