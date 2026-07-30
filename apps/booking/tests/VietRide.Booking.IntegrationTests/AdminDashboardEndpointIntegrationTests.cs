using System.Net;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Admin.Dashboard;
using VietRide.Booking.Application.Features.BookingStats.GetAdminBookingStatsAggregate;

namespace VietRide.Booking.IntegrationTests;

public sealed class AdminDashboardEndpointIntegrationTests
    : IClassFixture<BookingStatsWebApplicationFactory>
{
    private static readonly Guid OperatorId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private readonly BookingStatsWebApplicationFactory _factory;

    public AdminDashboardEndpointIntegrationTests(BookingStatsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Summary_SystemAdminReturnsEnvelopeAndExactComparisonShape()
    {
        ConfigureHappyPath();
        using var client = _factory.CreateAuthenticatedClient("SYSTEM_ADMIN");

        var response = await client.GetAsync(
            "/v1/admin/dashboard/summary?from=2026-07-01&to=2026-07-31");
        var responseBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, responseBody);
        using var document = JsonDocument.Parse(responseBody);
        var data = document.RootElement.GetProperty("data");
        data.GetProperty("period").GetProperty("timezone").GetString().Should().Be("Asia/Ho_Chi_Minh");
        data.GetProperty("totalRevenue").GetProperty("currentValue").GetInt64().Should().Be(1_500);
        data.GetProperty("totalRevenue").GetProperty("changePercent").GetDecimal().Should().Be(50m);
        data.GetProperty("activeOperators").GetProperty("currentValue").GetInt64().Should().Be(1);
        data.GetProperty("activeUsers").GetProperty("trend").GetString().Should().Be("UP");
        data.GetProperty("bookings").GetProperty("previousValue").GetInt64().Should().Be(5);
        data.GetProperty("userDistribution")[0].GetProperty("role").GetString().Should().Be("PASSENGER");
        data.GetProperty("operatorStatusDistribution")[0].GetProperty("percent").GetDecimal().Should().Be(100m);
    }

    [Fact]
    public async Task Summary_WrongRoleReturnsForbiddenBeforeDependencies()
    {
        _factory.BookingStatsRepository.ClearReceivedCalls();
        _factory.IdentityDashboard.ClearReceivedCalls();
        using var client = _factory.CreateAuthenticatedClient("OPERATOR_ADMIN", OperatorId);

        var response = await client.GetAsync(
            "/v1/admin/dashboard/summary?from=2026-07-01&to=2026-07-31");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _factory.BookingStatsRepository.DidNotReceiveWithAnyArgs()
            .GetAdminAggregateStatsAsync(default, default, default!, default);
        await _factory.IdentityDashboard.DidNotReceiveWithAnyArgs().GetAsync(default, default, default);
    }

    [Fact]
    public async Task Summary_InvalidRangeReturnsValidationEnvelope()
    {
        using var client = _factory.CreateAuthenticatedClient("SYSTEM_ADMIN");

        var response = await client.GetAsync(
            "/v1/admin/dashboard/summary?from=2026-08-01&to=2026-07-31");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task Summary_IdentityFailureReturns503WithoutPartialData()
    {
        var currentFrom = new DateOnly(2026, 7, 1);
        var currentTo = new DateOnly(2026, 7, 31);
        var previousFrom = new DateOnly(2026, 5, 31);
        var previousTo = new DateOnly(2026, 6, 30);
        _factory.BookingStatsRepository.GetAdminAggregateStatsAsync(
                currentFrom,
                currentTo,
                "operator",
                Arg.Any<CancellationToken>())
            .Returns([]);
        _factory.BookingStatsRepository.GetAdminAggregateStatsAsync(
                previousFrom,
                previousTo,
                "operator",
                Arg.Any<CancellationToken>())
            .Returns([]);
        _factory.IdentityDashboard.GetAsync(
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<IdentityDashboardMetricsDto>>(_ => throw new AdminDashboardUnavailableException());
        using var client = _factory.CreateAuthenticatedClient("SYSTEM_ADMIN");

        var response = await client.GetAsync(
            "/v1/admin/dashboard/summary?from=2026-07-01&to=2026-07-31");
        var responseBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, responseBody);
        using var document = JsonDocument.Parse(responseBody);
        document.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("UPSTREAM_UNAVAILABLE");
        document.RootElement.TryGetProperty("data", out _).Should().BeFalse();
    }

    private void ConfigureHappyPath()
    {
        var currentFrom = new DateOnly(2026, 7, 1);
        var currentTo = new DateOnly(2026, 7, 31);
        var previousFrom = new DateOnly(2026, 5, 31);
        var previousTo = new DateOnly(2026, 6, 30);
        _factory.BookingStatsRepository.GetAdminAggregateStatsAsync(
                currentFrom,
                currentTo,
                "operator",
                Arg.Any<CancellationToken>())
            .Returns(
            [
                Stats(10, 1_500),
            ]);
        _factory.BookingStatsRepository.GetAdminAggregateStatsAsync(
                previousFrom,
                previousTo,
                "operator",
                Arg.Any<CancellationToken>())
            .Returns(
            [
                Stats(5, 1_000),
            ]);
        _factory.IdentityDashboard.GetAsync(currentFrom, currentTo, Arg.Any<CancellationToken>())
            .Returns(new IdentityDashboardMetricsDto(
                20,
                [OperatorId],
                [new IdentityDashboardUserRoleCountDto("PASSENGER", 20)],
                [new IdentityDashboardOperatorStatusCountDto("APPROVED", 1)]));
        _factory.IdentityDashboard.GetAsync(previousFrom, previousTo, Arg.Any<CancellationToken>())
            .Returns(new IdentityDashboardMetricsDto(10, [OperatorId], [], []));
    }

    private static AdminBookingStatsAggregateReadModel Stats(int bookings, long revenue)
        => new(
            OperatorId,
            "Operator",
            Date: null,
            bookings,
            revenue,
            TotalCancellations: 0,
            TotalNoShows: 0,
            TotalCompleted: 0);
}
