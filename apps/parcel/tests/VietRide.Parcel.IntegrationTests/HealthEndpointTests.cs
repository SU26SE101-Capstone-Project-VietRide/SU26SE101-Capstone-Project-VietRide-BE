using System.Net;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace VietRide.Parcel.IntegrationTests;

/// Smoke tests for /health (liveness). Pattern mirrors Identity/Booking
/// integration tests — /health does NOT touch the DB so an unreachable
/// connection is fine for liveness only.
public class HealthEndpointTests : IClassFixture<VietRideWebApplicationFactory>
{
    private readonly VietRideWebApplicationFactory _factory;

    public HealthEndpointTests(VietRideWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetHealth_Returns200_WithServiceName()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Parcel");
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetString().Should().Be("ok");
        doc.RootElement.GetProperty("service").GetString().Should().Be("Parcel");
    }

    [Fact]
    public async Task GetPing_Returns200_WithServiceName()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/v1/ping");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be((int)HttpStatusCode.OK);
        doc.RootElement.GetProperty("data").GetProperty("service").GetString().Should().Be("Parcel");
    }
}

public class VietRideWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("INTERNAL_JWT_SECRET", "test-secret-at-least-32-chars-long-xxxxx");
        Environment.SetEnvironmentVariable("TRIP_USE_DEV_STUB", "true");
        Environment.SetEnvironmentVariable("PAYMENT_USE_DEV_STUB", "true");
        Environment.SetEnvironmentVariable("BOOKING_USE_DEV_STUB", "true");
        Environment.SetEnvironmentVariable("IDENTITY_USE_DEV_STUB", "true");
        builder.UseSetting("INTERNAL_JWT_SECRET", "test-secret-at-least-32-chars-long-xxxxx");
        builder.UseSetting("ConnectionStrings:Default", "Host=localhost;Port=5432;Database=test;Username=postgres;Password=postgres");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Trip:UseDevStub"] = "true",
                ["Payment:UseDevStub"] = "true",
                ["Booking:UseDevStub"] = "true",
                ["Identity:UseDevStub"] = "true",
            });
        });
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IConnectionMultiplexer>();
            services.AddSingleton(InMemoryRedisConnectionMultiplexer.Create());
        });
    }
}

internal class InMemoryRedisConnectionMultiplexer : DispatchProxy
{
    private static Dictionary<string, RedisValue> store = new();

    public static IConnectionMultiplexer Create()
    {
        store = new Dictionary<string, RedisValue>();
        return DispatchProxy.Create<IConnectionMultiplexer, InMemoryRedisConnectionMultiplexer>()!;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
        {
            return null;
        }

        return targetMethod.Name == nameof(IConnectionMultiplexer.GetDatabase)
            ? InMemoryRedisDatabase.Create()
            : targetMethod.ReturnType == typeof(void)
                ? null
                : targetMethod.ReturnType.IsValueType
                    ? Activator.CreateInstance(targetMethod.ReturnType)
                    : null;
    }

    private class InMemoryRedisDatabase : DispatchProxy
    {
        public static IDatabase Create()
            => DispatchProxy.Create<IDatabase, InMemoryRedisDatabase>()!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                return null;
            }

            return targetMethod.Name switch
            {
                nameof(IDatabase.KeyExistsAsync) => Task.FromResult(store.ContainsKey(Key(args![0]!))),
                nameof(IDatabase.StringGetAsync) => Task.FromResult(store.TryGetValue(Key(args![0]!), out var value) ? value : RedisValue.Null),
                nameof(IDatabase.StringSetAsync) => Task.FromResult(Set(Key(args![0]!), (RedisValue)args![1]!, (When)args![3]!)),
                _ => targetMethod.ReturnType == typeof(void)
                    ? null
                    : targetMethod.ReturnType.IsValueType
                        ? Activator.CreateInstance(targetMethod.ReturnType)
                        : null,
            };
        }

        private static string Key(object key) => key.ToString() ?? string.Empty;

        private static bool Set(string key, RedisValue value, When when)
        {
            if (when == When.NotExists && store.ContainsKey(key))
            {
                return false;
            }

            store[key] = value;
            return true;
        }
    }
}
