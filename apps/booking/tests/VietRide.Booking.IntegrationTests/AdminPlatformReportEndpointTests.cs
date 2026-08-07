using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Caching;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Admin.PlatformReports;
using VietRide.Booking.Application.Features.Internal.Reports.PlatformBookings;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.IntegrationTests;

public sealed class AdminPlatformReportEndpointTests
    : IClassFixture<AdminPlatformReportWebApplicationFactory>
{
    private static readonly Guid OperatorId =
        Guid.Parse("40000000-0000-4000-8000-000000000001");
    private readonly AdminPlatformReportWebApplicationFactory _factory;

    public AdminPlatformReportEndpointTests(AdminPlatformReportWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetReport_AsSystemAdmin_ReturnsReconciledEnvelope()
    {
        _factory.ConfigureReconciledSources(OperatorId);
        using var client = _factory.CreateAuthenticatedClient("SYSTEM_ADMIN");

        var response = await client.GetAsync(AdminPlatformReportWebApplicationFactory.ReportPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("statusCode").GetInt32().Should().Be(200);
        var data = document.RootElement.GetProperty("data");
        data.GetProperty("period").GetProperty("timezone").GetString()
            .Should().Be("Asia/Ho_Chi_Minh");
        data.GetProperty("totals").GetProperty("netTransportRevenueVnd").GetInt64()
            .Should().Be(150_000);
        var item = data.GetProperty("byOperator").EnumerateArray().Single();
        item.GetProperty("operatorId").GetGuid().Should().Be(OperatorId);
        item.GetProperty("operatorName").GetString().Should().Be("Nha xe A");
        document.RootElement.GetProperty("meta").TryGetProperty("traceId", out _)
            .Should().BeTrue();
    }

    [Fact]
    public async Task GetReport_WhenPaymentFails_ReturnsCanonical503WithoutPartialData()
    {
        _factory.ConfigureReconciledSources(OperatorId);
        _factory.LedgerClient.GetAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<PlatformLedgerReportItem>>(
                new HttpRequestException("payment unavailable")));
        using var client = _factory.CreateAuthenticatedClient("SYSTEM_ADMIN");

        var response = await client.GetAsync(AdminPlatformReportWebApplicationFactory.ReportPath);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("UPSTREAM_UNAVAILABLE");
        document.RootElement.TryGetProperty("data", out _).Should().BeFalse();
        await _factory.Cache.DidNotReceiveWithAnyArgs()
            .SetAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task GetReport_WhenPaidNoShowHasNoCompletedBooking_ReturnsLedgerRevenueAndPromotesCache()
    {
        _factory.ConfigureLedgerOnlyBookingRevenue(OperatorId, 450_000);
        using var client = _factory.CreateAuthenticatedClient("SYSTEM_ADMIN");

        var response = await client.GetAsync(AdminPlatformReportWebApplicationFactory.ReportPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        var totals = data.GetProperty("totals");
        totals.GetProperty("completedBookingCount").GetInt64().Should().Be(0);
        totals.GetProperty("netTicketRevenueVnd").GetInt64().Should().Be(450_000);
        totals.GetProperty("netTransportRevenueVnd").GetInt64().Should().Be(450_000);
        totals.TryGetProperty("bookingRevenueVnd", out _).Should().BeFalse();
        var item = data.GetProperty("byOperator").EnumerateArray().Single();
        item.GetProperty("completedBookingCount").GetInt64().Should().Be(0);
        item.GetProperty("netTicketRevenueVnd").GetInt64().Should().Be(450_000);
        await _factory.Cache.Received(1).SetAsync(
            Arg.Is<string>(key => key.StartsWith("platform-report:v3:", StringComparison.Ordinal)),
            Arg.Any<PlatformReportResult>(),
            TimeSpan.FromSeconds(60),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetReport_AsOperatorAdmin_Returns403BeforeQueryExecution()
    {
        _factory.ClearReceivedCalls();
        using var client = _factory.CreateAuthenticatedClient("OPERATOR_ADMIN", OperatorId);

        var response = await client.GetAsync(AdminPlatformReportWebApplicationFactory.ReportPath);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _factory.BookingRepository.DidNotReceiveWithAnyArgs()
            .GetPlatformBookingMetricsAsync(default, default, default);
        await _factory.TripClient.DidNotReceiveWithAnyArgs()
            .GetAsync(default, default, default);
    }
}

public sealed class AdminPlatformReportWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string TestSecret = "test-secret-at-least-32-chars-long-xxxxx";
    public const string ReportPath =
        "/v1/admin/reports/platform?from=2026-07-01&to=2026-07-31";

    public IBookingRepository BookingRepository { get; } = Substitute.For<IBookingRepository>();
    public ITripPlatformReportClient TripClient { get; } = Substitute.For<ITripPlatformReportClient>();
    public IParcelPlatformReportClient ParcelClient { get; } = Substitute.For<IParcelPlatformReportClient>();
    public IPaymentPlatformLedgerClient LedgerClient { get; } = Substitute.For<IPaymentPlatformLedgerClient>();
    public IIdentityPlatformReportClient IdentityClient { get; } = Substitute.For<IIdentityPlatformReportClient>();
    public IPlatformReportCache Cache { get; } = Substitute.For<IPlatformReportCache>();
    public IClock Clock { get; } = Substitute.For<IClock>();

    public AdminPlatformReportWebApplicationFactory()
    {
        Cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PlatformReportResult?>(null));
        Cache.SetAsync(
                Arg.Any<string>(),
                Arg.Any<PlatformReportResult>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        Clock.UtcNow.Returns(new DateTimeOffset(2026, 7, 29, 0, 0, 0, TimeSpan.Zero));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("INTERNAL_JWT_SECRET", TestSecret);
        builder.UseSetting("INTERNAL_JWT_SECRET", TestSecret);
        builder.UseSetting(
            "ConnectionStrings:Default",
            "Host=localhost;Port=5432;Database=test;Username=postgres;Password=postgres");
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            Replace(services, BookingRepository);
            Replace(services, TripClient);
            Replace(services, ParcelClient);
            Replace(services, LedgerClient);
            Replace(services, IdentityClient);
            Replace(services, Cache);
            Replace(services, Clock);
        });
    }

    public void ConfigureReconciledSources(Guid operatorId)
    {
        ClearReceivedCalls();
        BookingRepository.GetPlatformBookingMetricsAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns([new PlatformBookingReportItem(operatorId, 2, 100_000)]);
        TripClient.GetAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns([new TripPlatformReportItem(operatorId, 1)]);
        ParcelClient.GetAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns([new ParcelPlatformReportItem(operatorId, 3)]);
        LedgerClient.GetAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns([new PlatformLedgerReportItem(operatorId, 100_000, 50_000)]);
        IdentityClient.GetAsync(
                Arg.Any<IReadOnlyList<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns([new OperatorSummaryItem(operatorId, "Nha xe A")]);
    }

    public void ConfigureLedgerOnlyBookingRevenue(Guid operatorId, long bookingRevenueVnd)
    {
        ClearReceivedCalls();
        BookingRepository.GetPlatformBookingMetricsAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<PlatformBookingReportItem>>([]);
        TripClient.GetAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<TripPlatformReportItem>>([]);
        ParcelClient.GetAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ParcelPlatformReportItem>>([]);
        LedgerClient.GetAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns([new PlatformLedgerReportItem(operatorId, bookingRevenueVnd, 0)]);
        IdentityClient.GetAsync(
                Arg.Any<IReadOnlyList<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns([new OperatorSummaryItem(operatorId, "Nha xe A")]);
    }

    public void ClearReceivedCalls()
    {
        BookingRepository.ClearReceivedCalls();
        TripClient.ClearReceivedCalls();
        ParcelClient.ClearReceivedCalls();
        LedgerClient.ClearReceivedCalls();
        IdentityClient.ClearReceivedCalls();
        Cache.ClearReceivedCalls();
    }

    public HttpClient CreateAuthenticatedClient(string role, Guid? operatorId = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(
            "X-Internal-Auth",
            $"Bearer {MintInternalJwt(Guid.NewGuid().ToString(), role, operatorId)}");
        return client;
    }

    private static void Replace<TService>(IServiceCollection services, TService instance)
        where TService : class
    {
        services.RemoveAll<TService>();
        services.AddSingleton(instance);
    }

    private static string MintInternalJwt(string subject, string role, Guid? operatorId)
    {
        var now = DateTimeOffset.UtcNow;
        var header = Base64UrlEncode(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["alg"] = "HS256",
                ["typ"] = "JWT",
            })));
        var claims = new Dictionary<string, object?>
        {
            ["iss"] = "vietride-gateway",
            ["aud"] = "vietride-internal",
            ["sub"] = subject,
            ["role"] = role,
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddSeconds(120).ToUnixTimeSeconds(),
        };

        if (operatorId.HasValue)
        {
            claims["operator_id"] = operatorId.Value;
        }

        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(claims, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var signingInput = $"{header}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
        var signature = Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput)));
        return $"{signingInput}.{signature}";
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
