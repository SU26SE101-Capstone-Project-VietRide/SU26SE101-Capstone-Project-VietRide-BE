using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Infrastructure.ExternalClients;
using VietRide.Identity.Infrastructure.Persistence.Repositories;
using VietRide.Identity.Infrastructure.Security;
using VietRide.Identity.Infrastructure.Seed;

namespace VietRide.Identity.Infrastructure.DependencyInjection;

/// <summary>
/// Registers all Infrastructure-layer services: repositories, security services,
/// and external-client stubs.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Adds Identity Infrastructure services (repositories + security + email stub) to
    /// the DI container.  Call after <c>AddVietRideDbContext&lt;IdentityDbContext&gt;</c>
    /// and after binding <c>IdentityJwt</c> configuration.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
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
        services.AddScoped<IActivityLogRepository, ActivityLogRepository>();
        services.AddScoped<IOAuthIdentityRepository, OAuthIdentityRepository>();

        // ------------------------------------------------------------------
        // Startup seeders
        // ------------------------------------------------------------------
        services.AddScoped<BootstrapAdminSeeder>();

        // ------------------------------------------------------------------
        // Security services
        // ------------------------------------------------------------------
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<IAccessTokenService, RsaAccessTokenService>();
        services.AddSingleton<IJwksProvider, JwksProvider>();
        services.AddSingleton<IGoogleIdTokenVerifier, GoogleIdTokenVerifier>();
        services.AddScoped<IRefreshTokenFactory, RefreshTokenFactory>();
        services.AddSingleton<IOtpFailedAttemptPersister, OtpFailedAttemptPersister>();
        services.AddSingleton<IFailedLoginPersister, FailedLoginPersister>();
        services.AddSingleton<IRefreshTokenFamilyRevoker, RefreshTokenFamilyRevoker>();
        services.AddSingleton<ILoginLockoutCounter, RedisLoginLockoutCounter>();

        // ------------------------------------------------------------------
        // External-client stubs
        // ------------------------------------------------------------------
        // Day 3: LoggingEmailService logs OTP to Serilog.
        // Day 10: replaced by OutboxBackedEmailService (Outbox → Notification Service → SendGrid).
        services.AddSingleton<IEmailService, LoggingEmailService>();

        // ------------------------------------------------------------------
        // Redis — OTP rate-limit (BSOT §6.9). Falls back gracefully if not configured.
        // ------------------------------------------------------------------
        var redisUrl = configuration["REDIS_URL"] ?? "localhost:6379";
        var redisOptions = ConfigurationOptions.Parse(redisUrl);
        redisOptions.AbortOnConnectFail = false;
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisOptions));
        services.AddSingleton<IOtpRateLimiter, RedisOtpRateLimiter>();

        return services;
    }
}
