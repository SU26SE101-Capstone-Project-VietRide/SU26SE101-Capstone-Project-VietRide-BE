using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using VietRide.Identity.Api.Controllers;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Internal.AdminDashboard;
using VietRide.Shared.Web.Authentication;
using VietRide.Shared.Web.Filters;

namespace VietRide.Identity.IntegrationTests.Api;

public sealed class AdminDashboardIdentityMetricsEndpointTests
{
    private const string InternalJwtSecret = "identity-internal-test-secret-32-chars";

    [Fact]
    public async Task Endpoint_WithInternalJwt_ReturnsRawMetricsAndPassesVietnamRange()
    {
        var operatorId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var repository = new FakeAdminDashboardIdentityMetricsRepository
        {
            Result = new AdminDashboardIdentityMetricsReadResult(
                4,
                [operatorId],
                [new AdminDashboardIdentityMetricCountReadModel("PASSENGER", 7)],
                [new AdminDashboardIdentityMetricCountReadModel("APPROVED", 2)]),
        };
        await using var app = await CreateAppAsync(repository);
        using var client = app.GetTestClient();
        AddInternalJwt(client);

        var response = await client.GetAsync(
            "/internal/v1/admin/dashboard/identity-metrics?from=2026-02-01&to=2026-02-01");
        var responseBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, responseBody);
        responseBody.Should().NotContain("\"success\"");
        using var document = JsonDocument.Parse(responseBody);
        document.RootElement.GetProperty("activeUserCount").GetInt64().Should().Be(4);
        document.RootElement.GetProperty("approvedActiveOperatorIds")[0].GetGuid().Should().Be(operatorId);
        document.RootElement.GetProperty("userRoleCounts")[0].GetProperty("role").GetString().Should().Be("PASSENGER");
        document.RootElement.GetProperty("operatorStatusCounts")[0].GetProperty("status").GetString().Should().Be("APPROVED");
        repository.Calls.Should().ContainSingle();
        repository.Calls[0].FromUtc.Should().Be(DateTimeOffset.Parse("2026-01-31T17:00:00Z"));
        repository.Calls[0].ToUtcExclusive.Should().Be(DateTimeOffset.Parse("2026-02-01T17:00:00Z"));
    }

    [Fact]
    public async Task Endpoint_WithoutInternalJwt_ReturnsUnauthorized()
    {
        await using var app = await CreateAppAsync(new FakeAdminDashboardIdentityMetricsRepository());

        var response = await app.GetTestClient().GetAsync(
            "/internal/v1/admin/dashboard/identity-metrics?from=2026-02-01&to=2026-02-01");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Endpoint_WithInvalidRange_ReturnsValidationEnvelope()
    {
        await using var app = await CreateAppAsync(new FakeAdminDashboardIdentityMetricsRepository());
        using var client = app.GetTestClient();
        AddInternalJwt(client);

        var response = await client.GetAsync(
            "/internal/v1/admin/dashboard/identity-metrics?from=2026-02-02&to=2026-02-01");
        var responseBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, responseBody);
        using var document = JsonDocument.Parse(responseBody);
        document.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
    }

    private static async Task<WebApplication> CreateAppAsync(
        IAdminDashboardIdentityMetricsRepository repository)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services
            .AddControllers(options =>
            {
                options.Filters.Add<ApiResponseExceptionFilter>();
                options.Filters.Add<ApiResponseResultFilter>();
            })
            .AddApplicationPart(typeof(InternalAdminDashboardController).Assembly);
        builder.Services.AddAuthentication(InternalJwtAuthenticationExtensions.Scheme)
            .AddInternalJwt(InternalJwtSecret);
        builder.Services.AddAuthorization();
        builder.Services.AddMediatR(typeof(GetAdminDashboardIdentityMetricsQueryHandler).Assembly);
        builder.Services.AddSingleton(repository);
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    private static void AddInternalJwt(HttpClient client)
    {
        var now = DateTime.UtcNow;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(InternalJwtSecret));
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims: [new Claim(JwtRegisteredClaimNames.Sub, "ui17-test")],
            notBefore: now.AddSeconds(-5),
            expires: now.AddSeconds(120),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Add(
            InternalJwtAuthenticationExtensions.HeaderName,
            $"Bearer {new JwtSecurityTokenHandler().WriteToken(token)}");
    }

    private sealed class FakeAdminDashboardIdentityMetricsRepository
        : IAdminDashboardIdentityMetricsRepository
    {
        public AdminDashboardIdentityMetricsReadResult Result { get; init; } =
            new(0, [], [], []);

        public List<RepositoryCall> Calls { get; } = [];

        public Task<AdminDashboardIdentityMetricsReadResult> GetAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtcExclusive,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new RepositoryCall(fromUtc, toUtcExclusive));
            return Task.FromResult(Result);
        }
    }

    private sealed record RepositoryCall(
        DateTimeOffset FromUtc,
        DateTimeOffset ToUtcExclusive);
}
