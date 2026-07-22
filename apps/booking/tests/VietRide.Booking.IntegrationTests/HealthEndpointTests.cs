using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using VietRide.Testing;

namespace VietRide.Booking.IntegrationTests;

/// Smoke tests for /health (liveness) and /v1/ping (Wave A controller).
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
        body.Should().Contain("Booking");
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetString().Should().Be("ok");
        doc.RootElement.GetProperty("service").GetString().Should().Be("Booking");
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
        doc.RootElement.GetProperty("data").GetProperty("service").GetString().Should().Be("Booking");
    }

    [Fact]
    public async Task GetSwagger_IdempotencyContractMatchesRuntimeMetadata()
    {
        using var client = _factory.CreateClient();

        await IdempotencyOpenApiContractAssertions.AssertMatchesRuntimeMetadataAsync(client, _factory.Services);
    }
}

public class VietRideWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("INTERNAL_JWT_SECRET", "test-secret-at-least-32-chars-long-xxxxx");
        builder.UseSetting("INTERNAL_JWT_SECRET", "test-secret-at-least-32-chars-long-xxxxx");
        builder.UseSetting("ConnectionStrings:Default", "Host=localhost;Port=5432;Database=test;Username=postgres;Password=postgres");
        builder.UseEnvironment("Testing");
    }
}
