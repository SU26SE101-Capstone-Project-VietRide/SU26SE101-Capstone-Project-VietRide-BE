using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Features.RevenueAnalytics.Core;
using VietRide.Payment.Application.Features.RevenueAnalytics.Operator;

namespace VietRide.Payment.IntegrationTests.RevenueAnalytics;

public sealed class OperatorRevenueAnalyticsEndpointTests : IClassFixture<OperatorRevenueAnalyticsFactory>
{
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private readonly OperatorRevenueAnalyticsFactory factory;

    public OperatorRevenueAnalyticsEndpointTests(OperatorRevenueAnalyticsFactory factory)
    {
        this.factory = factory;
        factory.Reset();
    }

    [Fact]
    public async Task OperatorAdmin_ReturnsAdrEnvelopeAndUsesClaimTenant()
    {
        var tripId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        factory.Repository.GetOperatorRevenueLedgerAsync(
                OperatorId,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new OperatorRevenueLedgerReadModel(new DateOnly(2026, 7, 1), tripId, 700, 300, 2, 1),
            ]);
        factory.Trip.GetTripSummariesAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new TripRevenueSummaryItem(
                    tripId,
                    "COMPLETED",
                    DateTimeOffset.Parse("2026-07-15T00:00:00Z"),
                    routeId,
                    "Route A",
                    "Origin A",
                    "Destination A"),
            ]);
        factory.Trip.GetRoutePerformanceAsync(OperatorId, "2026-07", Arg.Any<CancellationToken>())
            .Returns(
            [
                new TripRoutePerformanceItem(routeId, "Route A", "Origin A", "Destination A", 4, 3),
            ]);
        using var client = factory.CreateRoleClient("OPERATOR_ADMIN", OperatorId);

        using var response = await client.GetAsync("/v1/operator/revenue/analytics?month=2026-07");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = document.RootElement.GetProperty("data");
        data.GetProperty("summary").GetProperty("netRevenueVnd").GetProperty("currentValue")
            .GetInt64().Should().Be(1_000);
        data.GetProperty("summary").TryGetProperty("totalRevenueVnd", out _).Should().BeFalse();
        data.GetProperty("monthly").GetArrayLength().Should().Be(12);
        data.GetProperty("routePerformance")[0].GetProperty("completionRatePercent")
            .GetDecimal().Should().Be(75m);
        await factory.Repository.Received(1).GetOperatorRevenueLedgerAsync(
            OperatorId,
            Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("SYSTEM_ADMIN", HttpStatusCode.Forbidden)]
    [InlineData("OPERATOR_STAFF", HttpStatusCode.Forbidden)]
    public async Task NonOperatorAdmin_IsRejected(string? role, HttpStatusCode expected)
    {
        using var client = role is null
            ? factory.CreateClient()
            : factory.CreateRoleClient(role, OperatorId);

        using var response = await client.GetAsync("/v1/operator/revenue/analytics?month=2026-07");

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task MissingOperatorClaim_ReturnsForbiddenEnvelope()
    {
        using var client = factory.CreateRoleClient("OPERATOR_ADMIN", null);

        using var response = await client.GetAsync("/v1/operator/revenue/analytics?month=2026-07");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, body);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task InvalidMonth_Returns422ValidationEnvelope()
    {
        using var client = factory.CreateRoleClient("OPERATOR_ADMIN", OperatorId);

        using var response = await client.GetAsync("/v1/operator/revenue/analytics?month=2026-7");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, body);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task MissingRequiredTripSummary_Returns503UpstreamUnavailable()
    {
        factory.Repository.GetOperatorRevenueLedgerAsync(
                OperatorId,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new OperatorRevenueLedgerReadModel(
                    new DateOnly(2026, 7, 1),
                    Guid.NewGuid(),
                    100,
                    0,
                    1,
                    0),
            ]);
        using var client = factory.CreateRoleClient("OPERATOR_ADMIN", OperatorId);

        using var response = await client.GetAsync("/v1/operator/revenue/analytics?month=2026-07");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, body);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("UPSTREAM_UNAVAILABLE");
    }

    [Fact]
    public async Task MediatRPipeline_ResolvesOperatorRevenueQueryAsReadOnly()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var act = () => sender.Send(new GetOperatorRevenueAnalyticsQuery(OperatorId, "invalid"));

        await act.Should().ThrowAsync<VietRide.Shared.Application.Exceptions.CodedValidationException>();
    }
}

public sealed class OperatorRevenueAnalyticsFactory : WebApplicationFactory<Program>
{
    public IRevenueAnalyticsRepository Repository { get; } = Substitute.For<IRevenueAnalyticsRepository>();
    public ITripRevenueAnalyticsClient Trip { get; } = Substitute.For<ITripRevenueAnalyticsClient>();

    public void Reset()
    {
        Repository.ClearReceivedCalls();
        Trip.ClearReceivedCalls();
        Repository.GetOperatorRevenueLedgerAsync(
                Arg.Any<Guid>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<OperatorRevenueLedgerReadModel>());
        Trip.GetRoutePerformanceAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TripRoutePerformanceItem>());
        Trip.GetTripSummariesAsync(
                Arg.Any<IReadOnlyList<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TripRevenueSummaryItem>());
    }

    public HttpClient CreateRoleClient(string role, Guid? operatorId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-Role", role);
        if (operatorId.HasValue)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "X-Test-Operator-Id",
                operatorId.Value.ToString("D"));
        }

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting(
            "ConnectionStrings:Default",
            "Host=localhost;Port=5432;Database=test;Username=postgres;Password=postgres");
        builder.UseSetting("INTERNAL_JWT_SECRET", "ui22-test-secret-at-least-32-characters");
        builder.UseSetting("InvoiceStorage:Provider", "E2E_LOCAL");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRevenueAnalyticsRepository>();
            services.RemoveAll<ITripRevenueAnalyticsClient>();
            services.RemoveAll<IRevenueReportCache>();
            services.RemoveAll<IConnectionMultiplexer>();
            services.AddSingleton(Repository);
            services.AddSingleton(Trip);
            services.AddSingleton<IRevenueReportCache, RevenueAnalyticsTestCache>();
            services.AddSingleton<IConnectionMultiplexer>(InMemoryIdempotencyRedis.Create());
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = OperatorRevenueTestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = OperatorRevenueTestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, OperatorRevenueTestAuthenticationHandler>(
                    OperatorRevenueTestAuthenticationHandler.SchemeName,
                    _ => { });
        });
    }
}

internal sealed class OperatorRevenueTestAuthenticationHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "OperatorRevenueTest";

    public OperatorRevenueTestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var role = Request.Headers["X-Test-Role"].ToString();
        if (string.IsNullOrWhiteSpace(role))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new(ClaimTypes.Role, role),
        };
        var operatorId = Request.Headers["X-Test-Operator-Id"].ToString();
        if (!string.IsNullOrWhiteSpace(operatorId))
        {
            claims.Add(new Claim("operator_id", operatorId));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, SchemeName)));
    }
}
