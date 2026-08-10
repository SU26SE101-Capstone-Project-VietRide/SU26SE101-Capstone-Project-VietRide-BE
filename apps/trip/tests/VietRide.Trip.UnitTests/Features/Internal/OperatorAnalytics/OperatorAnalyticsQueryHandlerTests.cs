using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Internal.OperatorAnalytics;

namespace VietRide.Trip.UnitTests.Features.Internal.OperatorAnalytics;

public sealed class OperatorAnalyticsQueryHandlerTests
{
    private readonly StubOperatorAnalyticsRepository _repository = new();

    [Fact]
    public async Task VehicleCounts_ReturnsEveryDistinctInputSortedAndZeroFilled()
    {
        var first = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var second = Guid.Parse("22222222-2222-4222-8222-222222222222");
        _repository.VehicleCounts = [new OperatorVehicleCountReadModel(second, 3)];
        var handler = new GetOperatorVehicleCountsQueryHandler(_repository);

        var result = await handler.Handle(
            new GetOperatorVehicleCountsQuery([second, first]),
            CancellationToken.None);

        result.Should().Equal(
            new OperatorVehicleCountResponse(first, 0),
            new OperatorVehicleCountResponse(second, 3));
        _repository.LastOperatorIds.Should().BeEquivalentTo([first, second]);
    }

    [Theory]
    [MemberData(nameof(InvalidOperatorIds))]
    public async Task VehicleCounts_RejectsInvalidIdsBeforeRepository(IReadOnlyList<Guid> ids)
    {
        var handler = new GetOperatorVehicleCountsQueryHandler(_repository);

        var act = () => handler.Handle(new GetOperatorVehicleCountsQuery(ids), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
        _repository.VehicleCountCallCount.Should().Be(0);
    }

    [Fact]
    public async Task RoutePerformance_UsesInclusiveVietnamMonthAndMapsSortedRows()
    {
        var operatorId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var routeA = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var routeB = Guid.Parse("22222222-2222-4222-8222-222222222222");
        _repository.RoutePerformance =
        [
            new OperatorRoutePerformanceReadModel(routeB, "B route", "Origin B", "Destination B", 2, 1),
            new OperatorRoutePerformanceReadModel(routeA, "A route", "Origin A", "Destination A", 3, 2),
        ];
        var handler = new GetOperatorRoutePerformanceQueryHandler(_repository);

        var result = await handler.Handle(
            new GetOperatorRoutePerformanceQuery(operatorId, "2026-07"),
            CancellationToken.None);

        result.Select(item => item.RouteId).Should().Equal(routeA, routeB);
        result[0].Should().Be(new OperatorRoutePerformanceResponse(
            routeA,
            "A route",
            "Origin A",
            "Destination A",
            3,
            2));
        _repository.LastRouteOperatorId.Should().Be(operatorId);
        _repository.LastFromUtc.Should().Be(DateTimeOffset.Parse("2026-06-30T17:00:00Z"));
        _repository.LastToUtc.Should().Be(DateTimeOffset.Parse("2026-07-31T17:00:00Z"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-7")]
    [InlineData("2026-00")]
    [InlineData("2026-13")]
    public async Task RoutePerformance_RejectsInvalidMonthBeforeRepository(string? month)
    {
        var handler = new GetOperatorRoutePerformanceQueryHandler(_repository);

        var act = () => handler.Handle(
            new GetOperatorRoutePerformanceQuery(Guid.NewGuid(), month),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
        _repository.RoutePerformanceCallCount.Should().Be(0);
    }

    public static IEnumerable<object[]> InvalidOperatorIds()
    {
        yield return [Array.Empty<Guid>()];
        yield return [new[] { Guid.Empty }];
        var duplicate = Guid.NewGuid();
        yield return [new[] { duplicate, duplicate }];
        yield return [Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray()];
    }

    private sealed class StubOperatorAnalyticsRepository : IOperatorAnalyticsRepository
    {
        public IReadOnlyList<OperatorVehicleCountReadModel> VehicleCounts { get; set; } = [];
        public IReadOnlyList<OperatorRoutePerformanceReadModel> RoutePerformance { get; set; } = [];
        public IReadOnlyCollection<Guid>? LastOperatorIds { get; private set; }
        public Guid? LastRouteOperatorId { get; private set; }
        public DateTimeOffset? LastFromUtc { get; private set; }
        public DateTimeOffset? LastToUtc { get; private set; }
        public int VehicleCountCallCount { get; private set; }
        public int RoutePerformanceCallCount { get; private set; }

        public Task<IReadOnlyList<OperatorVehicleCountReadModel>> GetVehicleCountsAsync(
            IReadOnlyCollection<Guid> operatorIds,
            CancellationToken cancellationToken)
        {
            VehicleCountCallCount++;
            LastOperatorIds = operatorIds;
            return Task.FromResult(VehicleCounts);
        }

        public Task<IReadOnlyList<OperatorRoutePerformanceReadModel>> GetRoutePerformanceAsync(
            Guid operatorId,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken)
        {
            RoutePerformanceCallCount++;
            LastRouteOperatorId = operatorId;
            LastFromUtc = fromUtc;
            LastToUtc = toUtc;
            return Task.FromResult(RoutePerformance);
        }
    }
}
