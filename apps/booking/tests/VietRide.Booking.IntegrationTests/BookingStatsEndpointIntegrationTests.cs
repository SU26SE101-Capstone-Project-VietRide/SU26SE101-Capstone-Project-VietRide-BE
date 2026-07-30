using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Admin.Dashboard;
using VietRide.Booking.Application.Features.BookingStats.GetAdminBookingStatsAggregate;
using VietRide.Booking.Application.Features.BookingStats.GetOperatorBookingStats;
using VietRide.Booking.Application.Features.OperatorBookings.GetOperatorBookingDetail;
using VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.IntegrationTests;

public sealed class BookingStatsEndpointIntegrationTests
    : IClassFixture<BookingStatsWebApplicationFactory>
{
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private readonly BookingStatsWebApplicationFactory _factory;

    public BookingStatsEndpointIntegrationTests(BookingStatsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetOperatorBookingStats_UsesOperatorClaimAndReturnsEnvelope()
    {
        _factory.BookingStatsRepository.ClearReceivedCalls();
        _factory.BookingStatsRepository.GetOperatorStatsAsync(
                OperatorId,
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 30),
                "date",
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new OperatorBookingStatsReadModel(
                    OperatorId,
                    new DateOnly(2026, 6, 26),
                    TotalBookings: 5,
                    TotalRevenue: 900_000,
                    TotalCancellations: 1,
                    TotalNoShows: 2,
                    TotalCompleted: 3),
            ]);
        var client = _factory.CreateAuthenticatedClient("OPERATOR_STAFF", OperatorId);

        var response = await client.GetAsync(
            "/v1/operator/booking-stats?from=2026-06-01&to=2026-06-30&groupBy=date&operatorId=bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var item = doc.RootElement
            .GetProperty("data")
            .GetProperty("items")[0];

        item.GetProperty("operatorId").GetGuid().Should().Be(OperatorId);
        item.GetProperty("totalRevenue").GetInt64().Should().Be(900_000);
        item.GetProperty("totalPartialNoShows").GetInt32().Should().Be(0);
        await _factory.BookingStatsRepository.Received(1).GetOperatorStatsAsync(
            OperatorId,
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            "date",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BookingStatsMonth_OperatorEndpoint_ReturnsZeroFilledEnvelopeAndSummary()
    {
        _factory.BookingStatsRepository.ClearReceivedCalls();
        _factory.BookingStatsRepository.GetOperatorStatsAsync(
                OperatorId,
                new DateOnly(2026, 1, 15),
                new DateOnly(2026, 3, 10),
                "month",
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new OperatorBookingStatsReadModel(
                    OperatorId,
                    new DateOnly(2026, 2, 1),
                    TotalBookings: 5,
                    TotalRevenue: 900_000,
                    TotalCancellations: 1,
                    TotalNoShows: 2,
                    TotalCompleted: 3),
            ]);
        var client = _factory.CreateAuthenticatedClient("OPERATOR_ADMIN", OperatorId);

        var response = await client.GetAsync(
            "/v1/operator/booking-stats?from=2026-01-15&to=2026-03-10&groupBy=month");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("items").GetArrayLength().Should().Be(3);
        data.GetProperty("items")[0].GetProperty("date").GetString().Should().Be("2026-01-01");
        data.GetProperty("items")[0].TryGetProperty("operatorId", out _).Should().BeFalse();
        data.GetProperty("items")[0].TryGetProperty("totalNoShows", out _).Should().BeFalse();
        data.GetProperty("items")[0].TryGetProperty("totalPartialNoShows", out _).Should().BeFalse();
        data.GetProperty("items")[0].GetProperty("totalCompleted").GetInt32().Should().Be(0);
        data.GetProperty("items")[0].GetProperty("totalBookings").GetInt32().Should().Be(0);
        data.GetProperty("totalBookings").GetInt32().Should().Be(5);
        data.GetProperty("totalRevenue").GetInt64().Should().Be(900_000);
    }

    [Fact]
    public async Task BookingStatsMonth_AdminEndpoint_ReturnsZeroFilledEnvelopeAndSummary()
    {
        _factory.BookingStatsRepository.ClearReceivedCalls();
        _factory.BookingStatsRepository.GetAdminAggregateStatsAsync(
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 2, 28),
                "month",
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new AdminBookingStatsAggregateReadModel(
                    OperatorId: null,
                    OperatorName: null,
                    Date: new DateOnly(2026, 2, 1),
                    TotalBookings: 7,
                    TotalRevenue: 1_250_000,
                    TotalCancellations: 1,
                    TotalNoShows: 2,
                    TotalCompleted: 4),
            ]);
        var client = _factory.CreateAuthenticatedClient("SYSTEM_ADMIN");

        var response = await client.GetAsync(
            "/v1/admin/booking-stats/aggregate?from=2026-01-01&to=2026-02-28&groupBy=month");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("items").GetArrayLength().Should().Be(2);
        data.GetProperty("items")[0].GetProperty("date").GetString().Should().Be("2026-01-01");
        data.GetProperty("items")[0].TryGetProperty("operatorId", out _).Should().BeFalse();
        data.GetProperty("items")[0].TryGetProperty("operatorName", out _).Should().BeFalse();
        data.GetProperty("items")[0].TryGetProperty("totalNoShows", out _).Should().BeFalse();
        data.GetProperty("items")[0].TryGetProperty("totalPartialNoShows", out _).Should().BeFalse();
        data.GetProperty("items")[0].TryGetProperty("totalCompleted", out _).Should().BeFalse();
        data.GetProperty("totalBookings").GetInt32().Should().Be(7);
        data.GetProperty("totalRevenue").GetInt64().Should().Be(1_250_000);
    }

    [Fact]
    public async Task GetAdminBookingStatsAggregate_WithOperatorRole_Returns403BeforeRepository()
    {
        _factory.BookingStatsRepository.ClearReceivedCalls();
        var client = _factory.CreateAuthenticatedClient("OPERATOR_ADMIN", OperatorId);

        var response = await client.GetAsync("/v1/admin/booking-stats/aggregate?groupBy=operator");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _factory.BookingStatsRepository.DidNotReceiveWithAnyArgs()
            .GetAdminAggregateStatsAsync(default, default, default!, default);
    }

    [Fact]
    public async Task GetOperatorBookingStats_WithUnsupportedGroupBy_ReturnsValidationEnvelope()
    {
        _factory.BookingStatsRepository.ClearReceivedCalls();
        var client = _factory.CreateAuthenticatedClient("OPERATOR_STAFF", OperatorId);

        var response = await client.GetAsync("/v1/operator/booking-stats?groupBy=operator");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCodeAsync(response, "VALIDATION_ERROR");
        await _factory.BookingStatsRepository.DidNotReceiveWithAnyArgs()
            .GetOperatorStatsAsync(default, default, default, default!, default);
    }

    [Fact]
    public async Task GetAdminBookingStatsAggregate_WithUnsupportedGroupBy_ReturnsValidationEnvelope()
    {
        _factory.BookingStatsRepository.ClearReceivedCalls();
        var client = _factory.CreateAuthenticatedClient("SYSTEM_ADMIN");

        var response = await client.GetAsync("/v1/admin/booking-stats/aggregate?groupBy=trip");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCodeAsync(response, "VALIDATION_ERROR");
        await _factory.BookingStatsRepository.DidNotReceiveWithAnyArgs()
            .GetAdminAggregateStatsAsync(default, default, default!, default);
    }

    private static async Task AssertErrorCodeAsync(HttpResponseMessage response, string expectedCode)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(expectedCode);
        doc.RootElement.GetProperty("meta").TryGetProperty("traceId", out _).Should().BeTrue();
    }
}

public sealed class BookingStatsWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string TestSecret = "test-secret-at-least-32-chars-long-xxxxx";

    public IBookingStatsRepository BookingStatsRepository { get; } = Substitute.For<IBookingStatsRepository>();
    public IBookingRepository BookingRepository { get; } = Substitute.For<IBookingRepository>();
    public IIdentityUserServiceClient IdentityUsers { get; } = Substitute.For<IIdentityUserServiceClient>();
    public IIdentityDashboardMetricsClient IdentityDashboard { get; } =
        Substitute.For<IIdentityDashboardMetricsClient>();
    public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

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
            services.AddSingleton(BookingStatsRepository);
            services.AddSingleton(BookingRepository);
            services.AddSingleton(IdentityUsers);
            services.AddSingleton(IdentityDashboard);

            var mockUow = UnitOfWork;
            mockUow.ExecuteInTransactionAsync(
                    Arg.Any<Func<Task<GetOperatorBookingStatsResult>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    var op = ci.Arg<Func<Task<GetOperatorBookingStatsResult>>>();
                    return op();
                });
            mockUow.ExecuteInTransactionAsync(
                    Arg.Any<Func<Task<GetAdminBookingStatsAggregateResult>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    var op = ci.Arg<Func<Task<GetAdminBookingStatsAggregateResult>>>();
                    return op();
                });
            mockUow.ExecuteInTransactionAsync(
                    Arg.Any<Func<Task<PagedResult<OperatorBookingListItem>>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(ci => ci.Arg<Func<Task<PagedResult<OperatorBookingListItem>>>>()());
            mockUow.ExecuteInTransactionAsync(
                    Arg.Any<Func<Task<OperatorBookingDetailDto>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(ci => ci.Arg<Func<Task<OperatorBookingDetailDto>>>()());
            services.AddSingleton(mockUow);
        });
    }

    public HttpClient CreateAuthenticatedClient(string role, Guid? operatorId = null)
    {
        var client = CreateClient();
        var token = MintInternalJwt(Guid.NewGuid().ToString(), role, operatorId);
        client.DefaultRequestHeaders.Add("X-Internal-Auth", $"Bearer {token}");
        return client;
    }

    private static string MintInternalJwt(string subject, string role, Guid? operatorId)
    {
        var secretBytes = Encoding.UTF8.GetBytes(TestSecret);
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
        using var hmac = new HMACSHA256(secretBytes);
        var sig = Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput)));
        return $"{signingInput}.{sig}";
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
