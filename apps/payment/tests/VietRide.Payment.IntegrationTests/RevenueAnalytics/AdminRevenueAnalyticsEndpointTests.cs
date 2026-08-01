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
using VietRide.Payment.Application.Features.Admin.PlatformReports;
using VietRide.Payment.Application.Features.RevenueAnalytics.Admin;
using VietRide.Payment.Application.Features.RevenueAnalytics.Core;

namespace VietRide.Payment.IntegrationTests.RevenueAnalytics;

public sealed class AdminRevenueAnalyticsEndpointTests : IClassFixture<AdminRevenueAnalyticsFactory>
{
    private readonly AdminRevenueAnalyticsFactory factory;

    public AdminRevenueAnalyticsEndpointTests(AdminRevenueAnalyticsFactory factory)
    {
        this.factory = factory;
        factory.Reset();
    }

    [Fact]
    public async Task SystemAdmin_ReturnsAdrEnvelopeAndDispatchesRealFacade()
    {
        var operatorId = Guid.NewGuid();
        factory.Repository.GetAdminMonthlyRevenueAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(
                [new AdminRevenueMonthReadModel(new DateOnly(2026, 7, 1), 700, 300)],
                Array.Empty<AdminRevenueMonthReadModel>());
        factory.Repository.GetTopOperatorPayoutsAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns([new TopOperatorPayoutReadModel(operatorId, 300)]);
        factory.Identity.GetAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new OperatorSummaryItem(operatorId, "Operator A", null)]);
        factory.Trip.GetVehicleCountsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new TripVehicleCountItem(operatorId, 4)]);
        using var client = factory.CreateRoleClient("SYSTEM_ADMIN");

        using var response = await client.GetAsync(
            "/v1/admin/revenue/analytics?from=2026-07-01&to=2026-07-31&groupBy=month&top=5");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = document.RootElement.GetProperty("data");
        data.GetProperty("summary").GetProperty("grossRevenueVnd").GetProperty("currentValue")
            .GetInt64().Should().Be(1_000);
        data.GetProperty("topOperators")[0].GetProperty("vehicleCount").GetInt32().Should().Be(4);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("OPERATOR_ADMIN", HttpStatusCode.Forbidden)]
    public async Task NonSystemAdmin_IsRejected(string? role, HttpStatusCode expected)
    {
        using var client = role is null ? factory.CreateClient() : factory.CreateRoleClient(role);

        using var response = await client.GetAsync(
            "/v1/admin/revenue/analytics?from=2026-07-01&to=2026-07-31&groupBy=month");

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task InvalidGroupBy_Returns422ValidationEnvelope()
    {
        using var client = factory.CreateRoleClient("SYSTEM_ADMIN");

        using var response = await client.GetAsync(
            "/v1/admin/revenue/analytics?from=2026-07-01&to=2026-07-31&groupBy=day");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, body);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task MissingRequiredEnrichment_Returns503UpstreamUnavailable()
    {
        var operatorId = Guid.NewGuid();
        factory.Repository.GetTopOperatorPayoutsAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns([new TopOperatorPayoutReadModel(operatorId, 100)]);
        factory.Trip.GetVehicleCountsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new TripVehicleCountItem(operatorId, 2)]);
        using var client = factory.CreateRoleClient("SYSTEM_ADMIN");

        using var response = await client.GetAsync(
            "/v1/admin/revenue/analytics?from=2026-07-01&to=2026-07-31&groupBy=month");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, body);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("UPSTREAM_UNAVAILABLE");
    }

    [Fact]
    public async Task MediatRPipeline_ResolvesAdminRevenueHandler()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var act = () => sender.Send(
            new GetAdminRevenueAnalyticsQuery("2026-07-01", "2026-07-31", "day", 5));

        await act.Should().ThrowAsync<VietRide.Shared.Application.Exceptions.CodedValidationException>();
    }
}

public sealed class AdminRevenueAnalyticsFactory : WebApplicationFactory<Program>
{
    public IRevenueAnalyticsRepository Repository { get; } = Substitute.For<IRevenueAnalyticsRepository>();
    public IIdentityOperatorSummaryClient Identity { get; } = Substitute.For<IIdentityOperatorSummaryClient>();
    public ITripRevenueAnalyticsClient Trip { get; } = Substitute.For<ITripRevenueAnalyticsClient>();

    public void Reset()
    {
        Repository.ClearReceivedCalls();
        Identity.ClearReceivedCalls();
        Trip.ClearReceivedCalls();
        Repository.GetAdminMonthlyRevenueAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AdminRevenueMonthReadModel>());
        Repository.GetTopOperatorPayoutsAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TopOperatorPayoutReadModel>());
        Identity.GetAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<OperatorSummaryItem>());
        Trip.GetVehicleCountsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TripVehicleCountItem>());
    }

    public HttpClient CreateRoleClient(string role)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Test-Role", role);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting(
            "ConnectionStrings:Default",
            "Host=localhost;Port=5432;Database=test;Username=postgres;Password=postgres");
        builder.UseSetting("INTERNAL_JWT_SECRET", "ui21-test-secret-at-least-32-characters");
        builder.UseSetting("InvoiceStorage:Provider", "E2E_LOCAL");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRevenueAnalyticsRepository>();
            services.RemoveAll<IIdentityOperatorSummaryClient>();
            services.RemoveAll<ITripRevenueAnalyticsClient>();
            services.RemoveAll<IConnectionMultiplexer>();
            services.AddSingleton(Repository);
            services.AddSingleton(Identity);
            services.AddSingleton(Trip);
            services.AddSingleton<IConnectionMultiplexer>(InMemoryIdempotencyRedis.Create());
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
        });
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "AdminRevenueTest";

        public TestAuthenticationHandler(
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

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role)],
                SchemeName));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
