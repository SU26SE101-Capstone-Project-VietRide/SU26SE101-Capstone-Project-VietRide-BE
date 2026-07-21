using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using VietRide.Shared.Web.Health;
using Xunit;

namespace VietRide.Shared.Web.UnitTests.Health;

public sealed class HealthCheckBuilderExtensionsTests
{
    [Fact]
    public void AddVietRideHealthChecks_WhenRedisUrlIsConfigured_RegistersRedisReadinessProbe()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["REDIS_URL"] = "redis:6379,abortConnect=false",
        });
        var services = new ServiceCollection();

        services.AddVietRideHealthChecks(configuration);

        var registration = GetRedisRegistration(services);
        registration.Tags.Should().Contain(new[] { "ready", "cache" });
    }

    [Fact]
    public void AddVietRideHealthChecks_WhenOnlyRedisHostIsConfigured_RegistersLegacyRedisReadinessProbe()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["REDIS_HOST"] = "redis",
            ["REDIS_PORT"] = "6379",
        });
        var services = new ServiceCollection();

        services.AddVietRideHealthChecks(configuration);

        GetRedisRegistration(services).Name.Should().Be("redis");
    }

    private static HealthCheckRegistration GetRedisRegistration(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        return options.Registrations.Should().ContainSingle(x => x.Name == "redis").Subject;
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
