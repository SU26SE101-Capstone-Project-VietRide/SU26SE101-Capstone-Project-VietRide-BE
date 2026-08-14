using System.Text.Json;
using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Internal.Trips.Cargo;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Domain.Exceptions;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Features.Internal.Trips.Cargo;

public sealed class Day29CargoNearFullProducerTests
{
    [Fact]
    public async Task ThresholdCrossing_EmitsExactlyOneCargoThresholdFact()
    {
        var fixture = new Fixture(new TripCargoMutationResult(Guid.NewGuid(), 0, 0, 80, 0, 100, 0, 80, true, Guid.NewGuid()));
        await fixture.Handler.Handle(fixture.Command("load"), default);

        fixture.Outbox.Messages.Should().ContainSingle();
        fixture.Outbox.Messages[0].EventType.Should().Be(CargoThresholdCrossedIntegrationEvent.EventTypeValue);
        using var payload = JsonDocument.Parse(fixture.Outbox.Messages[0].Payload);
        payload.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            ["eventId", "occurredAt", "tripId", "operatorId", "loadedWeightKg", "maxCargoWeightKg", "percentFull"]);
        payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(fixture.Outbox.Messages[0].EventId);
        payload.RootElement.GetProperty("occurredAt").GetDateTimeOffset().Should().Be(FixedClock.Now);
        payload.RootElement.GetProperty("tripId").GetGuid().Should().Be(fixture.Result.TripId);
        payload.RootElement.GetProperty("operatorId").GetGuid().Should().Be(fixture.Result.OperatorId);
        payload.RootElement.GetProperty("loadedWeightKg").GetDecimal().Should().Be(80m);
        payload.RootElement.GetProperty("maxCargoWeightKg").GetDecimal().Should().Be(100m);
        payload.RootElement.GetProperty("percentFull").GetDecimal().Should().Be(80m);
        fixture.UnitOfWork.Calls.Should().Equal("begin", "save", "commit");
    }

    [Fact]
    public async Task RemainingAboveThreshold_EmitsNoAdditionalCargoThresholdFact()
    {
        var fixture = new Fixture(new TripCargoMutationResult(Guid.NewGuid(), 0, 0, 90, 0, 100, 0, 90, false, Guid.NewGuid()));
        await fixture.Handler.Handle(fixture.Command("load"), default);
        fixture.Outbox.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task DuplicateParcelLoadReplay_EmitsNoAdditionalCargoThresholdFact()
    {
        var fixture = new Fixture(new TripCargoMutationResult(Guid.NewGuid(), 0, 0, 80, 0, 100, 0, 80, false, Guid.NewGuid()));
        await fixture.Handler.Handle(fixture.Command("load"), default);
        fixture.Outbox.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task ReleaseCargo_EmitsNoCargoThresholdFact()
    {
        var fixture = new Fixture(new TripCargoMutationResult(Guid.NewGuid(), 0, 0, 70, 0, 100, 0, 70, false, Guid.NewGuid()));
        await fixture.Handler.Handle(fixture.Command("release"), default);
        fixture.Outbox.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task RemeasureLoadedCargo_MapsStateConflictWithoutClaimingCapacityExceeded()
    {
        var fixture = new Fixture(
            new TripCargoMutationResult(Guid.NewGuid(), 0, 0, 30, 0.0034m, 500, 5, 6, false, Guid.NewGuid()),
            new InvalidOperationException("Only reserved cargo can be remeasured."));

        var action = () => fixture.Handler.Handle(fixture.Command("remeasure"), default);

        var exception = await action.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("TRIP_CARGO_STATE_INVALID");
        exception.Which.Message.Should().Be("Only reserved cargo can be remeasured.");
        fixture.UnitOfWork.Calls.Should().Equal("begin", "rollback");
        fixture.Outbox.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task CapacityFailure_RemainsCapacityExceeded()
    {
        var fixture = new Fixture(
            new TripCargoMutationResult(Guid.NewGuid(), 0, 0, 30, 0.0034m, 30, 5, 100, false, Guid.NewGuid()),
            new TripCargoCapacityExceededException("Trip cargo weight capacity would be exceeded."));

        var action = () => fixture.Handler.Handle(fixture.Command("remeasure"), default);

        var exception = await action.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("TRIP_CARGO_CAPACITY_EXCEEDED");
        fixture.UnitOfWork.Calls.Should().Equal("begin", "rollback");
    }

    private sealed class Fixture
    {
        public Fixture(TripCargoMutationResult result, Exception? remeasureException = null)
        {
            Result = result;
            Repository = new FakeRepository(result, remeasureException);
            Outbox = new FakeOutbox();
            UnitOfWork = new FakeUnitOfWork();
            Handler = new CargoMutationCommandHandler(Repository, Outbox, UnitOfWork, new FixedClock());
        }

        public TripCargoMutationResult Result { get; }
        public FakeRepository Repository { get; }
        public FakeOutbox Outbox { get; }
        public FakeUnitOfWork UnitOfWork { get; }
        public CargoMutationCommandHandler Handler { get; }
        public CargoMutationCommand Command(string action) => new(Result.TripId, Guid.NewGuid(), 10, 1, false, action);
    }

    private sealed class FixedClock : IClock
    {
        public static readonly DateTimeOffset Now = new(2026, 7, 22, 0, 0, 0, TimeSpan.Zero);
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public List<string> Calls { get; } = [];

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            Calls.Add("save");
            return Task.FromResult(1);
        }

        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
            => operation();

        public Task BeginTransactionAsync(CancellationToken cancellationToken)
        {
            Calls.Add("begin");
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            Calls.Add("commit");
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken)
        {
            Calls.Add("rollback");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOutbox : IIntegrationEventOutbox
    {
        public List<(Guid EventId, string EventType, string Payload)> Messages { get; } = [];

        public Task EnqueueAsync(Guid eventId, string eventType, string payloadJson, CancellationToken ct = default)
        {
            Messages.Add((eventId, eventType, payloadJson));
            return Task.CompletedTask;
        }

        public Task EnqueueAsync(string eventType, string payloadJson, CancellationToken ct = default)
            => EnqueueAsync(Guid.NewGuid(), eventType, payloadJson, ct);
    }

    private sealed class FakeRepository(
        TripCargoMutationResult result,
        Exception? remeasureException) : ITripRepository
    {
        public Task<TripCargoMutationResult?> LoadCargoAsync(Guid tripId, Guid parcelId, decimal weightKg, decimal volumeM3, bool allowCapacityOverflow, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<TripCargoMutationResult?>(result);
        public Task<TripCargoMutationResult?> ReleaseCargoAsync(Guid tripId, Guid parcelId, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<TripCargoMutationResult?>(result);
        public Task<TripCargoMutationResult?> RemeasureReservedCargoAsync(Guid tripId, Guid parcelId, decimal weightKg, decimal volumeM3, bool allowCapacityOverflow, DateTimeOffset now, CancellationToken cancellationToken)
            => remeasureException is null
                ? Task.FromResult<TripCargoMutationResult?>(result)
                : Task.FromException<TripCargoMutationResult?>(remeasureException);
        public Task<TripEntity?> GetByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<TripEntity> AddAsync(TripEntity entity, CancellationToken ct) => throw new NotSupportedException();
        public void Update(TripEntity entity) => throw new NotSupportedException();
        public void Remove(TripEntity entity) => throw new NotSupportedException();
        public IQueryable<TripEntity> Query() => throw new NotSupportedException();
        public IQueryable<TripEntity> QueryNoTracking() => throw new NotSupportedException();
        public Task<TripEntity?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
