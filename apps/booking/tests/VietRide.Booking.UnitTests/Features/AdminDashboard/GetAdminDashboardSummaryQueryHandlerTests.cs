using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Admin.Dashboard;
using VietRide.Booking.Application.Features.BookingStats.GetAdminBookingStatsAggregate;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.UnitTests.Features.AdminDashboard;

public sealed class GetAdminDashboardSummaryQueryHandlerTests
{
    private static readonly Guid ApprovedOperatorA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid ApprovedOperatorB = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid UnapprovedOperator = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private readonly IBookingStatsRepository _stats = Substitute.For<IBookingStatsRepository>();
    private readonly IIdentityDashboardMetricsClient _identity = Substitute.For<IIdentityDashboardMetricsClient>();

    [Fact]
    public async Task Handle_ComparesEqualPeriodsAndUsesCurrentApprovedOperatorsForBothPeriods()
    {
        var currentFrom = new DateOnly(2026, 7, 1);
        var currentTo = new DateOnly(2026, 7, 31);
        var previousFrom = new DateOnly(2026, 5, 31);
        var previousTo = new DateOnly(2026, 6, 30);
        _stats.GetAdminAggregateStatsAsync(currentFrom, currentTo, "operator", Arg.Any<CancellationToken>())
            .Returns(
            [
                Stats(ApprovedOperatorA, 10, 1_000),
                Stats(ApprovedOperatorB, 0, 500),
            ]);
        _stats.GetAdminAggregateStatsAsync(previousFrom, previousTo, "operator", Arg.Any<CancellationToken>())
            .Returns(
            [
                Stats(ApprovedOperatorA, 5, 800),
                Stats(UnapprovedOperator, 2, 200),
            ]);
        _identity.GetAsync(currentFrom, currentTo, Arg.Any<CancellationToken>())
            .Returns(new IdentityDashboardMetricsDto(
                20,
                [ApprovedOperatorB, ApprovedOperatorA],
                [
                    new IdentityDashboardUserRoleCountDto("PASSENGER", 18),
                    new IdentityDashboardUserRoleCountDto("DRIVER", 2),
                ],
                [
                    new IdentityDashboardOperatorStatusCountDto("APPROVED", 3),
                    new IdentityDashboardOperatorStatusCountDto("PENDING", 1),
                ]));
        _identity.GetAsync(previousFrom, previousTo, Arg.Any<CancellationToken>())
            .Returns(new IdentityDashboardMetricsDto(
                10,
                [ApprovedOperatorA, UnapprovedOperator],
                [],
                []));
        var handler = new GetAdminDashboardSummaryQueryHandler(_stats, _identity);

        var result = await handler.Handle(
            new GetAdminDashboardSummaryQuery(currentFrom, currentTo),
            CancellationToken.None);

        result.Period.Should().Be(new AdminDashboardPeriodResponse(
            currentFrom,
            currentTo,
            "Asia/Ho_Chi_Minh"));
        result.TotalRevenue.Should().Be(new AdminDashboardComparisonResponse(1_500, 1_000, 50m, "UP"));
        result.Bookings.Should().Be(new AdminDashboardComparisonResponse(10, 7, 42.86m, "UP"));
        result.ActiveUsers.Should().Be(new AdminDashboardComparisonResponse(20, 10, 100m, "UP"));
        result.ActiveOperators.Should().Be(new AdminDashboardComparisonResponse(1, 1, 0m, "FLAT"));
        result.UserDistribution.Select(item => (item.Role, item.Count)).Should().Equal(
            ("PASSENGER", 18L),
            ("DRIVER", 2L));
        result.OperatorStatusDistribution.Select(item => (item.Status, item.Count, item.Percent)).Should().Equal(
            ("APPROVED", 3L, 75m),
            ("PENDING", 1L, 25m));
    }

    [Fact]
    public async Task Handle_UsesZeroDenominatorRules()
    {
        var from = new DateOnly(2026, 7, 1);
        var to = new DateOnly(2026, 7, 1);
        var previous = new DateOnly(2026, 6, 30);
        _stats.GetAdminAggregateStatsAsync(from, to, "operator", Arg.Any<CancellationToken>())
            .Returns([Stats(ApprovedOperatorA, 1, 100)]);
        _stats.GetAdminAggregateStatsAsync(previous, previous, "operator", Arg.Any<CancellationToken>())
            .Returns([]);
        _identity.GetAsync(from, to, Arg.Any<CancellationToken>())
            .Returns(new IdentityDashboardMetricsDto(1, [ApprovedOperatorA], [], []));
        _identity.GetAsync(previous, previous, Arg.Any<CancellationToken>())
            .Returns(new IdentityDashboardMetricsDto(0, [], [], []));
        var handler = new GetAdminDashboardSummaryQueryHandler(_stats, _identity);

        var result = await handler.Handle(
            new GetAdminDashboardSummaryQuery(from, to),
            CancellationToken.None);

        result.TotalRevenue.Should().Be(new AdminDashboardComparisonResponse(100, 0, 0m, "UP"));
        result.Bookings.Should().Be(new AdminDashboardComparisonResponse(1, 0, 0m, "UP"));
        result.ActiveUsers.Should().Be(new AdminDashboardComparisonResponse(1, 0, 0m, "UP"));
        result.ActiveOperators.Should().Be(new AdminDashboardComparisonResponse(1, 0, 0m, "UP"));
    }

    [Theory]
    [InlineData(null, "2026-07-31")]
    [InlineData("2026-07-01", null)]
    [InlineData("2026-08-01", "2026-07-31")]
    [InlineData("2025-01-01", "2026-01-02")]
    public async Task Handle_RejectsInvalidRangeBeforeDependencies(string? fromValue, string? toValue)
    {
        var handler = new GetAdminDashboardSummaryQueryHandler(_stats, _identity);
        DateOnly? from = fromValue is null ? null : DateOnly.Parse(fromValue);
        DateOnly? to = toValue is null ? null : DateOnly.Parse(toValue);

        var act = () => handler.Handle(
            new GetAdminDashboardSummaryQuery(from, to),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
        await _stats.DidNotReceiveWithAnyArgs()
            .GetAdminAggregateStatsAsync(default, default, default!, default);
        await _identity.DidNotReceiveWithAnyArgs().GetAsync(default, default, default);
    }

    private static AdminBookingStatsAggregateReadModel Stats(Guid operatorId, int bookings, long revenue)
        => new(
            operatorId,
            "Operator",
            Date: null,
            bookings,
            revenue,
            TotalCancellations: 0,
            TotalNoShows: 0,
            TotalCompleted: 0);
}
