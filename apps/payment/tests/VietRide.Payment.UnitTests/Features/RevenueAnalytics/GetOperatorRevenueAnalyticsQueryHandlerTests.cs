using FluentAssertions;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.Admin.PlatformReports;
using VietRide.Payment.Application.Features.RevenueAnalytics.Core;
using VietRide.Payment.Application.Features.RevenueAnalytics.Operator;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.UnitTests.Features.RevenueAnalytics;

public sealed class GetOperatorRevenueAnalyticsQueryHandlerTests
{
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid TripA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid TripB = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid TripPrevious = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid RouteA = Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111");
    private static readonly Guid RouteB = Guid.Parse("bbbbbbbb-2222-4222-8222-222222222222");
    private static readonly Guid RouteC = Guid.Parse("cccccccc-3333-4333-8333-333333333333");
    private readonly StubRevenueRepository repository = new();
    private readonly StubTripClient trip = new();

    [Fact]
    public async Task Handle_BuildsTwelveMonthsComparisonsAndRoutePerformance()
    {
        repository.Rows =
        [
            new OperatorRevenueLedgerReadModel(new DateOnly(2026, 7, 1), TripA, 1_000, 300, 2, 1),
            new OperatorRevenueLedgerReadModel(new DateOnly(2026, 7, 1), TripB, -100, 200, 1, 2),
            new OperatorRevenueLedgerReadModel(new DateOnly(2026, 6, 1), TripPrevious, 600, 0, 1, 0),
        ];
        trip.Summaries =
        [
            Summary(TripB, RouteB, "Route B", "Origin B", "Destination B"),
            Summary(TripPrevious, RouteA, "Route A old", "Origin A", "Destination A"),
            Summary(TripA, RouteA, "Route A", "Origin A", "Destination A"),
        ];
        trip.Routes =
        [
            new TripRoutePerformanceItem(RouteC, "Route C", "Origin C", "Destination C", 2, 2),
            new TripRoutePerformanceItem(RouteA, "Route A", "Origin A", "Destination A", 5, 4),
            new TripRoutePerformanceItem(RouteB, "Route B", "Origin B", "Destination B", 0, 0),
        ];
        var handler = CreateHandler();

        var result = await handler.Handle(
            new GetOperatorRevenueAnalyticsQuery(OperatorId, "2026-07"),
            CancellationToken.None);

        result.Period.Should().Be(new OperatorRevenueAnalyticsPeriod(
            "2026-07",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31),
            RevenueAnalyticsPeriodRules.Timezone));
        result.Summary.TotalRevenueVnd.Should().Be(new RevenueComparison(1_400, 600, 133.33m, "UP"));
        result.Summary.TicketRevenueVnd.Should().Be(new RevenueComparison(900, 600, 50m, "UP"));
        result.Summary.ParcelRevenueVnd.Should().Be(new RevenueComparison(500, 0, 0m, "UP"));
        result.Summary.AverageRevenuePerTripVnd.Should().Be(new RevenueComparison(700, 600, 16.67m, "UP"));
        result.Monthly.Should().HaveCount(12);
        result.Monthly.Select(item => item.Month).Should().StartWith("2025-08").And.EndWith("2026-07");
        result.Monthly.Single(item => item.Month == "2026-06").Should().Be(
            new OperatorRevenueMonthItem("2026-06", 600, 600, 0, 1));
        result.Monthly.Single(item => item.Month == "2026-07").Should().Be(
            new OperatorRevenueMonthItem("2026-07", 1_400, 900, 500, 2));
        result.Monthly.Where(item => item.Month is not "2026-06" and not "2026-07")
            .Should().OnlyContain(item => item.RevenueVnd == 0 && item.TripCount == 0);
        result.RoutePerformance.Should().Equal(
            new OperatorRoutePerformanceItem(
                RouteA, "Route A", "Origin A", "Destination A", 5, 4, 2, 1, 1_300, 80m),
            new OperatorRoutePerformanceItem(
                RouteB, "Route B", "Origin B", "Destination B", 0, 0, 1, 2, 100, 0m),
            new OperatorRoutePerformanceItem(
                RouteC, "Route C", "Origin C", "Destination C", 2, 2, 0, 0, 0, 100m));
        repository.LastOperatorId.Should().Be(OperatorId);
        repository.LastFrom.Should().Be(DateTimeOffset.Parse("2025-07-31T17:00:00Z"));
        repository.LastTo.Should().Be(DateTimeOffset.Parse("2026-07-31T17:00:00Z"));
        trip.LastRouteOperatorId.Should().Be(OperatorId);
        trip.LastMonth.Should().Be("2026-07");
        trip.LastTripIds.Should().BeEquivalentTo([TripA, TripB, TripPrevious]);
    }

    [Fact]
    public async Task Handle_UsesCheckedAwayFromZeroAverageAndIncludesRevenueWithoutTrip()
    {
        repository.Rows =
        [
            new OperatorRevenueLedgerReadModel(new DateOnly(2026, 7, 1), TripA, 2, 0, 1, 0),
            new OperatorRevenueLedgerReadModel(new DateOnly(2026, 7, 1), TripB, 3, 0, 1, 0),
            new OperatorRevenueLedgerReadModel(new DateOnly(2026, 7, 1), null, 0, 2, 0, 1),
        ];
        trip.Summaries =
        [
            Summary(TripA, RouteA, "Route A", "Origin A", "Destination A"),
            Summary(TripB, RouteA, "Route A", "Origin A", "Destination A"),
        ];
        trip.Routes =
        [
            new TripRoutePerformanceItem(RouteA, "Route A", "Origin A", "Destination A", 2, 1),
        ];

        var result = await CreateHandler().Handle(
            new GetOperatorRevenueAnalyticsQuery(OperatorId, "2026-07"),
            CancellationToken.None);

        result.Summary.TotalRevenueVnd.CurrentValue.Should().Be(7);
        result.Summary.AverageRevenuePerTripVnd.CurrentValue.Should().Be(4);
        result.Monthly[^1].TripCount.Should().Be(2);
        result.RoutePerformance.Single().RevenueVnd.Should().Be(5);
    }

    [Fact]
    public async Task Handle_MissingRequiredTripSummaryFailsWholeResponse()
    {
        repository.Rows =
        [
            new OperatorRevenueLedgerReadModel(new DateOnly(2026, 7, 1), TripA, 100, 0, 1, 0),
        ];
        trip.Summaries = [];

        var act = () => CreateHandler().Handle(
            new GetOperatorRevenueAnalyticsQuery(OperatorId, "2026-07"),
            CancellationToken.None);

        await act.Should().ThrowAsync<UpstreamUnavailableException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-7")]
    [InlineData("2026-13")]
    public async Task Handle_InvalidMonthFailsBeforeRepository(string? month)
    {
        var act = () => CreateHandler().Handle(
            new GetOperatorRevenueAnalyticsQuery(OperatorId, month),
            CancellationToken.None);

        await act.Should().ThrowAsync<CodedValidationException>();
        repository.CallCount.Should().Be(0);
        trip.RouteCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_EmptyLedgerReturnsTwelveZeroMonthsWithoutTripSummaryCall()
    {
        trip.Routes =
        [
            new TripRoutePerformanceItem(RouteA, "Route A", "Origin A", "Destination A", 0, 0),
        ];

        var result = await CreateHandler().Handle(
            new GetOperatorRevenueAnalyticsQuery(OperatorId, "2026-07"),
            CancellationToken.None);

        result.Monthly.Should().HaveCount(12).And.OnlyContain(item => item.RevenueVnd == 0);
        result.Summary.TotalRevenueVnd.Should().Be(new RevenueComparison(0, 0, 0m, "FLAT"));
        result.RoutePerformance.Should().ContainSingle().Which.CompletionRatePercent.Should().Be(0);
        trip.SummaryCallCount.Should().Be(0);
    }

    private GetOperatorRevenueAnalyticsQueryHandler CreateHandler() => new(repository, trip);

    private static TripRevenueSummaryItem Summary(
        Guid tripId,
        Guid routeId,
        string routeName,
        string originName,
        string destinationName)
        => new(tripId, "COMPLETED", DateTimeOffset.Parse("2026-07-15T00:00:00Z"), routeId, routeName, originName, destinationName);

    private sealed class StubRevenueRepository : IRevenueAnalyticsRepository
    {
        public IReadOnlyList<OperatorRevenueLedgerReadModel> Rows { get; set; } = [];
        public int CallCount { get; private set; }
        public Guid? LastOperatorId { get; private set; }
        public DateTimeOffset? LastFrom { get; private set; }
        public DateTimeOffset? LastTo { get; private set; }

        public Task<IReadOnlyList<OperatorRevenueLedgerReadModel>> GetOperatorRevenueLedgerAsync(
            Guid operatorId,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastOperatorId = operatorId;
            LastFrom = fromUtc;
            LastTo = toUtc;
            return Task.FromResult(Rows);
        }

        public Task<IReadOnlyList<AdminRevenueMonthReadModel>> GetAdminMonthlyRevenueAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<TopOperatorPayoutReadModel>> GetTopOperatorPayoutsAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            int top,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubTripClient : ITripRevenueAnalyticsClient
    {
        public IReadOnlyList<TripRoutePerformanceItem> Routes { get; set; } = [];
        public IReadOnlyList<TripRevenueSummaryItem> Summaries { get; set; } = [];
        public int RouteCallCount { get; private set; }
        public int SummaryCallCount { get; private set; }
        public Guid? LastRouteOperatorId { get; private set; }
        public string? LastMonth { get; private set; }
        public IReadOnlyList<Guid> LastTripIds { get; private set; } = [];

        public Task<IReadOnlyList<TripRoutePerformanceItem>> GetRoutePerformanceAsync(
            Guid operatorId,
            string month,
            CancellationToken cancellationToken = default)
        {
            RouteCallCount++;
            LastRouteOperatorId = operatorId;
            LastMonth = month;
            return Task.FromResult(Routes);
        }

        public Task<IReadOnlyList<TripRevenueSummaryItem>> GetTripSummariesAsync(
            IReadOnlyList<Guid> tripIds,
            CancellationToken cancellationToken = default)
        {
            SummaryCallCount++;
            LastTripIds = tripIds;
            return Task.FromResult(Summaries);
        }

        public Task<IReadOnlyList<TripVehicleCountItem>> GetVehicleCountsAsync(
            IReadOnlyList<Guid> operatorIds,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
