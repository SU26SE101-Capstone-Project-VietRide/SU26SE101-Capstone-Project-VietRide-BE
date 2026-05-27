using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace VietRide.Identity.IntegrationTests;

/// Smoke tests for /health (liveness) and /v1/ping (Wave A controller).
/// Wires WebApplicationFactory<Program> with the minimum env required by AddVietRideSharedWeb +
/// AddVietRideDbContext (INTERNAL_JWT_SECRET ≥32 chars + a placeholder Default connection string —
/// /health does NOT touch the DB so an unreachable connection is fine for liveness only).
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
        body.Should().Contain("Identity");
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetString().Should().Be("ok");
        doc.RootElement.GetProperty("service").GetString().Should().Be("Identity");
    }

    [Fact]
    public async Task GetPing_Returns200_WithServiceName()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/v1/ping");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("service").GetString().Should().Be("Identity");
    }
}

public class VietRideWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Provide just enough config to let Program.cs boot. /health is wired before auth
        // and does not exercise the DB, so an unreachable Postgres URL is acceptable here.
        Environment.SetEnvironmentVariable("INTERNAL_JWT_SECRET", "test-secret-at-least-32-chars-long-xxxxx");
        builder.UseSetting("INTERNAL_JWT_SECRET", "test-secret-at-least-32-chars-long-xxxxx");
        builder.UseSetting("ConnectionStrings:Default", "Host=localhost;Port=5432;Database=test;Username=postgres;Password=postgres");
        builder.UseEnvironment("Testing");
    }
}
