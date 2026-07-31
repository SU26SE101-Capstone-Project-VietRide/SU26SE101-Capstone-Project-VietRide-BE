using FluentAssertions;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.Admin.PlatformReports;
using VietRide.Payment.Application.Features.RevenueAnalytics.Admin;
using VietRide.Payment.Application.Features.RevenueAnalytics.Core;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.UnitTests.Features.RevenueAnalytics;

public sealed class GetAdminRevenueAnalyticsQueryHandlerTests
{
    private static readonly DateTimeOffset CurrentFrom = DateTimeOffset.Parse("2026-06-30T17:00:00Z");
    private static readonly DateTimeOffset CurrentTo = DateTimeOffset.Parse("2026-07-31T17:00:00Z");
    private static readonly DateTimeOffset PreviousFrom = DateTimeOffset.Parse("2026-05-30T17:00:00Z");
    private static readonly DateTimeOffset PreviousTo = CurrentFrom;
    private readonly StubRevenueRepository repository = new();
    private readonly StubIdentityClient identity = new();
    private readonly StubTripClient trip = new();

    [Fact]
    public async Task Handle_BuildsComparisonsZeroFilledMonthsAndEnrichedTopOperators()
    {
        var firstOperator = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var secondOperator = Guid.Parse("22222222-2222-4222-8222-222222222222");
        repository.MonthlyResolver = (from, to) => from == CurrentFrom && to == CurrentTo
            ? [new AdminRevenueMonthReadModel(new DateOnly(2026, 7, 1), 700, 400)]
            : from == PreviousFrom && to == PreviousTo
                ? [new AdminRevenueMonthReadModel(new DateOnly(2026, 6, 1), 500, 300)]
                : [];
        repository.TopRows =
            [
                new TopOperatorPayoutReadModel(firstOperator, 300),
                new TopOperatorPayoutReadModel(secondOperator, 100),
            ];
        identity.Result =
            [
                new OperatorSummaryItem(secondOperator, "Operator B"),
                new OperatorSummaryItem(firstOperator, "Operator A", "https://cdn.test/a.png"),
            ];
        trip.VehicleCounts =
            [
                new TripVehicleCountItem(firstOperator, 5),
                new TripVehicleCountItem(secondOperator, 0),
            ];
        var handler = CreateHandler();

        var result = await handler.Handle(
            new GetAdminRevenueAnalyticsQuery("2026-07-01", "2026-07-31", "month", 99),
            CancellationToken.None);

        result.Summary.GrossRevenueVnd.Should().Be(new RevenueComparison(1_100, 800, 37.5m, "UP"));
        result.Summary.PlatformRevenueVnd.Should().Be(new RevenueComparison(700, 500, 40m, "UP"));
        result.Summary.PaidToOperatorsVnd.Should().Be(new RevenueComparison(400, 300, 33.33m, "UP"));
        result.Monthly.Should().ContainSingle().Which.Should().Be(
            new AdminRevenueMonthItem("2026-07", 1_100, 400, 700));
        result.TopOperators.Should().Equal(
            new AdminTopOperatorItem(1, firstOperator, "Operator A", "https://cdn.test/a.png", 300, 5),
            new AdminTopOperatorItem(2, secondOperator, "Operator B", null, 100, 0));
        repository.LastTop.Should().Be(20);
        repository.LastTopFrom.Should().Be(CurrentFrom);
        repository.LastTopTo.Should().Be(CurrentTo);
    }

    [Fact]
    public async Task Handle_ZeroFillsEveryTouchedMonthAndReconcilesSummary()
    {
        var monthlyCalls = 0;
        repository.MonthlyResolver = (_, _) => ++monthlyCalls == 1
            ? [
                new AdminRevenueMonthReadModel(new DateOnly(2026, 2, 1), 20, 30),
            ]
            : [];
        var handler = CreateHandler();

        var result = await handler.Handle(
            new GetAdminRevenueAnalyticsQuery("2026-01-15", "2026-03-10", "month", null),
            CancellationToken.None);

        result.Monthly.Select(item => item.Month).Should().Equal("2026-01", "2026-02", "2026-03");
        result.Monthly.Select(item => item.GrossRevenueVnd).Should().Equal(0, 50, 0);
        result.Monthly.Sum(item => item.GrossRevenueVnd)
            .Should().Be(result.Summary.GrossRevenueVnd.CurrentValue);
        identity.CallCount.Should().Be(0);
        trip.VehicleCountCallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(null, "2026-07-31", "month")]
    [InlineData("2026-07-01", "2026-07-31", null)]
    [InlineData("2026-07-01", "2026-07-31", "day")]
    [InlineData("07/01/2026", "2026-07-31", "month")]
    public async Task Handle_RejectsInvalidContractBeforeRepository(string? from, string? to, string? groupBy)
    {
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new GetAdminRevenueAnalyticsQuery(from, to, groupBy, 5),
            CancellationToken.None);

        await act.Should().ThrowAsync<CodedValidationException>();
        repository.MonthlyCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_MissingRequiredTopEnrichmentFailsWholeFacade()
    {
        var operatorId = Guid.NewGuid();
        repository.TopRows = [new TopOperatorPayoutReadModel(operatorId, 100)];
        identity.Result = [];
        trip.VehicleCounts = [new TripVehicleCountItem(operatorId, 2)];
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new GetAdminRevenueAnalyticsQuery("2026-07-01", "2026-07-31", "month", 5),
            CancellationToken.None);

        await act.Should().ThrowAsync<UpstreamUnavailableException>();
    }

    private GetAdminRevenueAnalyticsQueryHandler CreateHandler()
        => new(repository, identity, trip);

    private sealed class StubRevenueRepository : IRevenueAnalyticsRepository
    {
        public Func<DateTimeOffset, DateTimeOffset, IReadOnlyList<AdminRevenueMonthReadModel>> MonthlyResolver { get; set; }
            = (_, _) => [];
        public IReadOnlyList<TopOperatorPayoutReadModel> TopRows { get; set; } = [];
        public int MonthlyCallCount { get; private set; }
        public int? LastTop { get; private set; }
        public DateTimeOffset? LastTopFrom { get; private set; }
        public DateTimeOffset? LastTopTo { get; private set; }

        public Task<IReadOnlyList<AdminRevenueMonthReadModel>> GetAdminMonthlyRevenueAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken = default)
        {
            MonthlyCallCount++;
            return Task.FromResult(MonthlyResolver(fromUtc, toUtc));
        }

        public Task<IReadOnlyList<TopOperatorPayoutReadModel>> GetTopOperatorPayoutsAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            int top,
            CancellationToken cancellationToken = default)
        {
            LastTop = top;
            LastTopFrom = fromUtc;
            LastTopTo = toUtc;
            return Task.FromResult(TopRows);
        }

        public Task<IReadOnlyList<OperatorRevenueLedgerReadModel>> GetOperatorRevenueLedgerAsync(
            Guid operatorId,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubIdentityClient : IIdentityOperatorSummaryClient
    {
        public IReadOnlyList<OperatorSummaryItem> Result { get; set; } = [];
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<OperatorSummaryItem>> GetAsync(
            IReadOnlyList<Guid> operatorIds,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(Result);
        }
    }

    private sealed class StubTripClient : ITripRevenueAnalyticsClient
    {
        public IReadOnlyList<TripVehicleCountItem> VehicleCounts { get; set; } = [];
        public int VehicleCountCallCount { get; private set; }

        public Task<IReadOnlyList<TripVehicleCountItem>> GetVehicleCountsAsync(
            IReadOnlyList<Guid> operatorIds,
            CancellationToken cancellationToken = default)
        {
            VehicleCountCallCount++;
            return Task.FromResult(VehicleCounts);
        }

        public Task<IReadOnlyList<TripRoutePerformanceItem>> GetRoutePerformanceAsync(
            Guid operatorId,
            string month,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<TripRevenueSummaryItem>> GetTripSummariesAsync(
            IReadOnlyList<Guid> tripIds,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
