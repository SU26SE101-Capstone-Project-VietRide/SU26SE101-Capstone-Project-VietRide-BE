using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
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

namespace VietRide.Payment.IntegrationTests;

public sealed class AdminPlatformReportEndpointTests
    : IClassFixture<PlatformReportWebApplicationFactory>
{
    private readonly PlatformReportWebApplicationFactory _factory;

    public AdminPlatformReportEndpointTests(PlatformReportWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetPlatformReport_IsNotExposedByPaymentAfterBookingOwnershipTransfer()
    {
        _factory.Reset();
        using var client = _factory.CreateRoleClient("SYSTEM_ADMIN");

        var response = await client.GetAsync(
            "/v1/admin/reports/platform" +
            "?from=2026-07-01T00%3A00%3A00Z" +
            "&to=2026-08-01T00%3A00%3A00Z");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await _factory.Bookings.DidNotReceiveWithAnyArgs().GetAsync(default, default, default!);
        await _factory.Trips.DidNotReceiveWithAnyArgs().GetAsync(default, default, default!);
        await _factory.Parcels.DidNotReceiveWithAnyArgs().GetAsync(default, default, default!);
        await _factory.Identity.DidNotReceiveWithAnyArgs().GetAsync(default!, default!);
        await _factory.Ledger.DidNotReceiveWithAnyArgs()
            .GetPlatformLedgerMetricsAsync(default, default, default!);
    }

    [Fact]
    public async Task GetPlatformReport_LegacyPaymentRouteDoesNotInvokeLedgerOrIdentity()
    {
        _factory.Reset();
        using var client = _factory.CreateRoleClient("SYSTEM_ADMIN");

        var response = await client.GetAsync(
            "/v1/admin/reports/platform?from=2026-07-01T00%3A00%3A00Z&to=2026-08-01T00%3A00%3A00Z");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await _factory.Bookings.DidNotReceiveWithAnyArgs().GetAsync(default, default, default!);
        await _factory.Identity.DidNotReceiveWithAnyArgs().GetAsync(default!, default!);
    }

    [Fact]
    public async Task GetPlatformReport_LegacyPaymentRouteIsAbsentForEveryRoleAndRange()
    {
        _factory.Reset();
        using var operatorClient = _factory.CreateRoleClient("OPERATOR_ADMIN");
        using var adminClient = _factory.CreateRoleClient("SYSTEM_ADMIN");

        var forbidden = await operatorClient.GetAsync(
            "/v1/admin/reports/platform?from=2026-07-01T00%3A00%3A00Z&to=2026-08-01T00%3A00%3A00Z");
        var invalid = await adminClient.GetAsync(
            "/v1/admin/reports/platform?from=2026-07-01T00%3A00%3A00%2B00%3A00&to=2026-08-01T00%3A00%3A00Z");

        forbidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        invalid.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await _factory.Bookings.DidNotReceiveWithAnyArgs().GetAsync(default, default, default!);
        await _factory.Trips.DidNotReceiveWithAnyArgs().GetAsync(default, default, default!);
        await _factory.Parcels.DidNotReceiveWithAnyArgs().GetAsync(default, default, default!);
    }

    [Fact]
    public async Task GetPlatformReport_LegacyPaymentRouteCannotReachUpstreamClients()
    {
        _factory.Reset();
        using var client = _factory.CreateRoleClient("SYSTEM_ADMIN");

        var response = await client.GetAsync(
            "/v1/admin/reports/platform?from=2026-07-01T00%3A00%3A00Z&to=2026-08-01T00%3A00%3A00Z");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await _factory.Bookings.DidNotReceiveWithAnyArgs().GetAsync(default, default, default!);
        await _factory.Identity.DidNotReceiveWithAnyArgs().GetAsync(default!, default!);
    }

}

public sealed class PlatformReportWebApplicationFactory : WebApplicationFactory<Program>
{
    public IBookingPlatformReportClient Bookings { get; } =
        Substitute.For<IBookingPlatformReportClient>();
    public ITripPlatformReportClient Trips { get; } =
        Substitute.For<ITripPlatformReportClient>();
    public IParcelPlatformReportClient Parcels { get; } =
        Substitute.For<IParcelPlatformReportClient>();
    public IIdentityOperatorSummaryClient Identity { get; } =
        Substitute.For<IIdentityOperatorSummaryClient>();
    public IOperatorLedgerEntryRepository Ledger { get; } =
        Substitute.For<IOperatorLedgerEntryRepository>();

    public void Reset()
    {
        Bookings.ClearReceivedCalls();
        Trips.ClearReceivedCalls();
        Parcels.ClearReceivedCalls();
        Identity.ClearReceivedCalls();
        Ledger.ClearReceivedCalls();
        Bookings.GetAsync(default, default, default!)
            .ReturnsForAnyArgs(Array.Empty<BookingPlatformReportItem>());
        Trips.GetAsync(default, default, default!)
            .ReturnsForAnyArgs(Array.Empty<TripPlatformReportItem>());
        Parcels.GetAsync(default, default, default!)
            .ReturnsForAnyArgs(Array.Empty<ParcelPlatformReportItem>());
        Identity.GetAsync(default!, default!)
            .ReturnsForAnyArgs(Array.Empty<OperatorSummaryItem>());
        Ledger.GetPlatformLedgerMetricsAsync(default, default, default!)
            .ReturnsForAnyArgs(Array.Empty<PlatformLedgerReportItem>());
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
        builder.UseSetting(
            "INTERNAL_JWT_SECRET",
            "day40-platform-report-test-secret-at-least-32-characters");
        builder.UseSetting("InvoiceStorage:Provider", "E2E_LOCAL");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IBookingPlatformReportClient>();
            services.RemoveAll<ITripPlatformReportClient>();
            services.RemoveAll<IParcelPlatformReportClient>();
            services.RemoveAll<IIdentityOperatorSummaryClient>();
            services.RemoveAll<IOperatorLedgerEntryRepository>();
            services.RemoveAll<IConnectionMultiplexer>();
            services.AddSingleton(Bookings);
            services.AddSingleton(Trips);
            services.AddSingleton(Parcels);
            services.AddSingleton(Identity);
            services.AddSingleton(Ledger);
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

    private sealed class TestAuthenticationHandler
        : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "PlatformReportTest";

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
                [
                    new Claim("sub", Guid.NewGuid().ToString()),
                    new Claim(ClaimTypes.Role, role),
                ],
                SchemeName));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
