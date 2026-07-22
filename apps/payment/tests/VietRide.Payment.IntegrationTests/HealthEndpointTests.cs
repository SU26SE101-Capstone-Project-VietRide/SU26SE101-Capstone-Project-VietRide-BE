using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using VietRide.Testing;

namespace VietRide.Payment.IntegrationTests;

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
        body.Should().Contain("Payment");
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetString().Should().Be("ok");
        doc.RootElement.GetProperty("service").GetString().Should().Be("Payment");
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
        doc.RootElement.GetProperty("data").GetProperty("service").GetString().Should().Be("Payment");
    }

    [Fact]
    public async Task GetSwagger_Returns200()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        body.Should().Contain("BatchChargePaymentRequestItem");
        body.Should().Contain("BatchChargePaymentResultItem");
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
