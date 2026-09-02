using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Trips.GetOperatorCargoCapacity;

namespace VietRide.Trip.UnitTests.Features.Trips;

public sealed class GetOperatorCargoCapacityQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReleasedLoadedCargo_ReturnsHistoricalTotalsWhileCurrentTotalsAreZero()
    {
        var operatorId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var handler = CreateHandler(new OperatorCargoCapacityReadModel(
            tripId,
            operatorId,
            0m,
            0m,
            0m,
            0m,
            500m,
            5m,
            12.5m,
            0.2m));

        var result = await handler.Handle(
            new GetOperatorCargoCapacityQuery(tripId, operatorId),
            CancellationToken.None);

        result.LoadedWeightKg.Should().Be(0m);
        result.LoadedVolumeM3.Should().Be(0m);
        result.HistoricalLoadedWeightKg.Should().Be(12.5m);
        result.HistoricalLoadedVolumeM3.Should().Be(0.2m);
    }

    [Fact]
    public async Task Handle_ActiveLoadedCargo_ReturnsCurrentAndHistoricalTotals()
    {
        var operatorId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var handler = CreateHandler(new OperatorCargoCapacityReadModel(
            tripId,
            operatorId,
            0m,
            0m,
            12.5m,
            0.2m,
            500m,
            5m,
            12.5m,
            0.2m));

        var result = await handler.Handle(
            new GetOperatorCargoCapacityQuery(tripId, operatorId),
            CancellationToken.None);

        result.LoadedWeightKg.Should().Be(12.5m);
        result.LoadedVolumeM3.Should().Be(0.2m);
        result.HistoricalLoadedWeightKg.Should().Be(12.5m);
        result.HistoricalLoadedVolumeM3.Should().Be(0.2m);
    }

    [Fact]
    public async Task Handle_ReservedOnlyCargo_DoesNotAppearInHistoricalTotals()
    {
        var operatorId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var handler = CreateHandler(new OperatorCargoCapacityReadModel(
            tripId,
            operatorId,
            8m,
            0.1m,
            0m,
            0m,
            500m,
            5m,
            0m,
            0m));

        var result = await handler.Handle(
            new GetOperatorCargoCapacityQuery(tripId, operatorId),
            CancellationToken.None);

        result.ReservedWeightKg.Should().Be(8m);
        result.ReservedVolumeM3.Should().Be(0.1m);
        result.HistoricalLoadedWeightKg.Should().Be(0m);
        result.HistoricalLoadedVolumeM3.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_ForeignOperator_ThrowsForbidden()
    {
        var tripId = Guid.NewGuid();
        var handler = CreateHandler(CreateEmptyReadModel(tripId, Guid.NewGuid()));

        var action = () => handler.Handle(
            new GetOperatorCargoCapacityQuery(tripId, Guid.NewGuid()),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ForbiddenException>();
        exception.Which.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task Handle_MissingTrip_ThrowsTripNotFound()
    {
        var handler = CreateHandler(null);

        var action = () => handler.Handle(
            new GetOperatorCargoCapacityQuery(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be("TRIP_NOT_FOUND");
    }

    private static GetOperatorCargoCapacityQueryHandler CreateHandler(OperatorCargoCapacityReadModel? readModel)
        => new(new StubRepository(readModel));

    private static OperatorCargoCapacityReadModel CreateEmptyReadModel(Guid tripId, Guid operatorId)
        => new(tripId, operatorId, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m);

    private sealed class StubRepository(OperatorCargoCapacityReadModel? readModel)
        : IOperatorCargoCapacityReadRepository
    {
        public Task<OperatorCargoCapacityReadModel?> GetAsync(
            Guid tripId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(readModel);
    }
}
