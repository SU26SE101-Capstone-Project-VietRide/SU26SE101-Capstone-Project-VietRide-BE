using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Infrastructure;
using VietRide.Identity.Infrastructure.DependencyInjection;
using VietRide.Identity.Infrastructure.ExternalClients;

namespace VietRide.Identity.IntegrationTests.DependencyInjection;

public sealed class IdentityInfrastructureDiResolutionTests
{
    [Fact]
    public void AddInfrastructure_ResolvesTaskFiveBaselineServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddScoped<IdentityDbContext>(_ => null!);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["REDIS_URL"] = "localhost:6379",
                ["PUBLIC_APP_URL"] = "https://test.vietride.app",
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IUserDeviceRepository>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IInitialPasswordTokenService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IEmailService>().Should().NotBeNull();
    }

    [Fact]
    public void AddInfrastructure_WhenEmailProviderIsSendGrid_UsesNotificationDelivery()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IdentityDbContext>(_ => null!);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["REDIS_URL"] = "localhost:6379",
                ["EMAIL_PROVIDER"] = "SENDGRID",
                ["EMAIL_SERVICE_BASE_URL"] = "http://notification:3002",
                ["PUBLIC_APP_URL"] = "https://test.vietride.app",
                ["InternalJwt:Secret"] = "integration-test-internal-jwt-secret-at-least-32-bytes",
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IEmailService>()
            .Should().BeOfType<NotificationEmailService>();
    }
}
