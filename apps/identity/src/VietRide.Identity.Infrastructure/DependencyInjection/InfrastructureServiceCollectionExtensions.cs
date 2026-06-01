using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Infrastructure.ExternalClients;
using VietRide.Identity.Infrastructure.Persistence.Repositories;
using VietRide.Identity.Infrastructure.Security;

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
        services.Configure<JwtSigningOptions>(
            configuration.GetSection(JwtSigningOptions.SectionName));

        // ------------------------------------------------------------------
        // Repositories
        // ------------------------------------------------------------------
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IEmailVerificationTokenRepository, EmailVerificationTokenRepository>();

        // ------------------------------------------------------------------
        // Security services
        // ------------------------------------------------------------------
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<IAccessTokenService, RsaAccessTokenService>();
        services.AddSingleton<IJwksProvider, JwksProvider>();
        services.AddScoped<IRefreshTokenFactory, RefreshTokenFactory>();

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
