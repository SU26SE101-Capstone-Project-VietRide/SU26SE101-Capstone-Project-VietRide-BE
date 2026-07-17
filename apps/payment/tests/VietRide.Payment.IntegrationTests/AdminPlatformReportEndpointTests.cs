using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
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
    public async Task GetPlatformReport_SystemAdminReturnsEnvelopeWithUnionAndSignedTotals()
    {
        _factory.Reset();
        var operatorA = Guid.Parse("40000000-0000-0000-0000-000000000001");
        var operatorB = Guid.Parse("40000000-0000-0000-0000-000000000002");
        _factory.Bookings.GetAsync(default, default, default!)
            .ReturnsForAnyArgs(new[] { new BookingPlatformReportItem(operatorA, 2, 500_000) });
        _factory.Trips.GetAsync(default, default, default!)
            .ReturnsForAnyArgs(new[] { new TripPlatformReportItem(operatorB, 3) });
        _factory.Parcels.GetAsync(default, default, default!)
            .ReturnsForAnyArgs(new[] { new ParcelPlatformReportItem(operatorA, 1, -50_000) });
        _factory.Identity.GetAsync(default!, default!)
            .ReturnsForAnyArgs(new[] { new OperatorSummaryItem(operatorA, "Operator A") });
        using var client = _factory.CreateRoleClient("SYSTEM_ADMIN");

        var response = await client.GetAsync(
            "/v1/admin/reports/platform" +
            "?from=2026-07-01T00%3A00%3A00Z" +
            "&to=2026-08-01T00%3A00%3A00Z");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = root.GetProperty("data");
        data.GetProperty("period").GetProperty("from").GetString()
            .Should().Be("2026-07-01T00:00:00Z");
        var totals = data.GetProperty("totals");
        totals.GetProperty("completedBookingCount").GetInt64().Should().Be(2);
        totals.GetProperty("completedTripCount").GetInt64().Should().Be(3);
        totals.GetProperty("parcelRevenueVnd").GetInt64().Should().Be(-50_000);
        totals.GetProperty("netRevenueVnd").GetInt64().Should().Be(450_000);
        var operators = data.GetProperty("byOperator").EnumerateArray().ToArray();
        operators.Should().HaveCount(2);
        operators[0].GetProperty("operatorId").GetGuid().Should().Be(operatorA);
        operators[0].GetProperty("operatorName").GetString().Should().Be("Operator A");
        operators[1].GetProperty("operatorId").GetGuid().Should().Be(operatorB);
        operators[1].GetProperty("operatorName").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetPlatformReport_EnforcesRbacAndRangeBeforeCallingSources()
    {
        _factory.Reset();
        using var operatorClient = _factory.CreateRoleClient("OPERATOR_ADMIN");
        using var adminClient = _factory.CreateRoleClient("SYSTEM_ADMIN");

        var forbidden = await operatorClient.GetAsync(
            "/v1/admin/reports/platform?from=2026-07-01T00%3A00%3A00Z&to=2026-08-01T00%3A00%3A00Z");
        var invalid = await adminClient.GetAsync(
            "/v1/admin/reports/platform?from=2026-07-01T00%3A00%3A00%2B00%3A00&to=2026-08-01T00%3A00%3A00Z");

        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCodeAsync(invalid, "VALIDATION_ERROR");
        await _factory.Bookings.DidNotReceiveWithAnyArgs().GetAsync(default, default, default!);
        await _factory.Trips.DidNotReceiveWithAnyArgs().GetAsync(default, default, default!);
        await _factory.Parcels.DidNotReceiveWithAnyArgs().GetAsync(default, default, default!);
    }

    [Theory]
    [InlineData(true, HttpStatusCode.InternalServerError, "REPORT_VALUE_OVERFLOW")]
    [InlineData(false, HttpStatusCode.BadGateway, "UPSTREAM_UNAVAILABLE")]
    public async Task GetPlatformReport_MapsUpstreamFailuresWithoutPartialResponse(
        bool overflow,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        _factory.Reset();
        Exception upstreamException = overflow
            ? new PlatformReportValueOverflowException()
            : new UpstreamUnavailableException();
        _factory.Bookings.GetAsync(default, default, default!)
            .ReturnsForAnyArgs(Task.FromException<IReadOnlyList<BookingPlatformReportItem>>(
                upstreamException));
        using var client = _factory.CreateRoleClient("SYSTEM_ADMIN");

        var response = await client.GetAsync(
            "/v1/admin/reports/platform?from=2026-07-01T00%3A00%3A00Z&to=2026-08-01T00%3A00%3A00Z");

        response.StatusCode.Should().Be(expectedStatus);
        await AssertErrorCodeAsync(response, expectedCode);
        await _factory.Identity.DidNotReceiveWithAnyArgs().GetAsync(default!, default!);
    }

    private static async Task AssertErrorCodeAsync(
        HttpResponseMessage response,
        string expectedCode)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be(expectedCode);
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

    public void Reset()
    {
        Bookings.ClearReceivedCalls();
        Trips.ClearReceivedCalls();
        Parcels.ClearReceivedCalls();
        Identity.ClearReceivedCalls();
        Bookings.GetAsync(default, default, default!)
            .ReturnsForAnyArgs(Array.Empty<BookingPlatformReportItem>());
        Trips.GetAsync(default, default, default!)
            .ReturnsForAnyArgs(Array.Empty<TripPlatformReportItem>());
        Parcels.GetAsync(default, default, default!)
            .ReturnsForAnyArgs(Array.Empty<ParcelPlatformReportItem>());
        Identity.GetAsync(default!, default!)
            .ReturnsForAnyArgs(Array.Empty<OperatorSummaryItem>());
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
            services.RemoveAll<IConnectionMultiplexer>();
            services.AddSingleton(Bookings);
            services.AddSingleton(Trips);
            services.AddSingleton(Parcels);
            services.AddSingleton(Identity);
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
