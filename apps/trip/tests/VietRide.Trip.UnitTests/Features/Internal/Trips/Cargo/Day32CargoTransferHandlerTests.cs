using System.Text.Json;
using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Internal.Trips.Cargo;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Features.Internal.Trips.Cargo;

public sealed class Day32CargoTransferHandlerTests
{
    [Fact]
    public async Task ReservedTransfer_ReturnsFrozenLedgerValuesAndCommits()
    {
        var result = Success("RESERVED", nearFullCrossed: false);
        var fixture = new Fixture(result);

        var response = await fixture.Handler.Handle(fixture.Command("RESERVED"), default);

        response.Should().Be(new CargoTransferDto(
            result.ParcelId,
            result.SourceTripId,
            result.TargetTripId,
            "RESERVED",
            12.5m,
            0.08m));
        fixture.Repository.LastAllowCapacityOverflow.Should().BeTrue();
        fixture.UnitOfWork.Calls.Should().Equal("begin", "save", "commit");
        fixture.Outbox.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadedTransfer_CrossingThreshold_EnqueuesCanonicalCargoFact()
    {
        var result = Success("LOADED", nearFullCrossed: true);
        var fixture = new Fixture(result);

        await fixture.Handler.Handle(fixture.Command("LOADED"), default);

        fixture.Outbox.Messages.Should().ContainSingle();
        fixture.Outbox.Messages[0].EventType.Should().Be(CargoThresholdCrossedIntegrationEvent.EventTypeValue);
        using var payload = JsonDocument.Parse(fixture.Outbox.Messages[0].Payload);
        payload.RootElement.GetProperty("tripId").GetGuid().Should().Be(result.TargetTripId);
        payload.RootElement.GetProperty("operatorId").GetGuid().Should().Be(result.TargetOperatorId);
        payload.RootElement.GetProperty("loadedWeightKg").GetDecimal().Should().Be(80m);
    }

    [Fact]
    public async Task CapacityFailure_ReturnsCanonical422CodeAndRollsBack()
    {
        var fixture = new Fixture(TripCargoTransferRepositoryResult.Failed(
            TripCargoTransferStatus.CAPACITY_EXCEEDED));

        var action = () => fixture.Handler.Handle(fixture.Command("RESERVED"), default);

        var exception = await action.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("TRIP_CARGO_CAPACITY_EXCEEDED");
        fixture.UnitOfWork.Calls.Should().Equal("begin", "rollback");
    }

    [Fact]
    public async Task UnverifiedOverflowFlag_ReturnsValidationErrorAndRollsBack()
    {
        var fixture = new Fixture(TripCargoTransferRepositoryResult.Failed(
            TripCargoTransferStatus.OVERFLOW_NOT_ALLOWED));

        var action = () => fixture.Handler.Handle(fixture.Command("LOADED"), default);

        var exception = await action.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
        fixture.UnitOfWork.Calls.Should().Equal("begin", "rollback");
    }

    [Fact]
    public async Task CrossOperatorOrRace_ReturnsCanonical409Code()
    {
        var fixture = new Fixture(TripCargoTransferRepositoryResult.Failed(
            TripCargoTransferStatus.CONFLICT));

        var action = () => fixture.Handler.Handle(fixture.Command("LOADED"), default);

        var exception = await action.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("TRIP_CARGO_TRANSFER_CONFLICT");
    }

    [Theory]
    [InlineData(TripCargoTransferStatus.TRIP_NOT_FOUND, "TRIP_NOT_FOUND")]
    [InlineData(TripCargoTransferStatus.SOURCE_CARGO_NOT_FOUND, "PARCEL_CARGO_NOT_FOUND")]
    public async Task MissingTripOrSourceCargo_ReturnsCanonical404Code(
        TripCargoTransferStatus status,
        string expectedCode)
    {
        var fixture = new Fixture(TripCargoTransferRepositoryResult.Failed(status));

        var action = () => fixture.Handler.Handle(fixture.Command("RESERVED"), default);

        var exception = await action.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be(expectedCode);
    }

    [Fact]
    public async Task SameSourceAndTarget_ReturnsConflictWithoutOpeningTransaction()
    {
        var result = Success("RESERVED", nearFullCrossed: false);
        var fixture = new Fixture(result);
        var command = fixture.Command("RESERVED") with
        {
            TargetTripId = result.SourceTripId,
        };

        var action = () => fixture.Handler.Handle(command, default);

        var exception = await action.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("TRIP_CARGO_TRANSFER_CONFLICT");
        fixture.Repository.CallCount.Should().Be(0);
        fixture.UnitOfWork.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task InvalidTargetState_ReturnsValidationErrorWithoutOpeningTransaction()
    {
        var fixture = new Fixture(Success("RESERVED", nearFullCrossed: false));

        var action = () => fixture.Handler.Handle(fixture.Command("reserved"), default);

        var exception = await action.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
        fixture.Repository.CallCount.Should().Be(0);
        fixture.UnitOfWork.Calls.Should().BeEmpty();
    }

    private static TripCargoTransferRepositoryResult Success(
        string targetState,
        bool nearFullCrossed)
    {
        var sourceTripId = Guid.NewGuid();
        var targetTripId = Guid.NewGuid();
        return new TripCargoTransferRepositoryResult(
            TripCargoTransferStatus.SUCCESS,
            Guid.NewGuid(),
            sourceTripId,
            targetTripId,
            targetState,
            12.5m,
            0.08m,
            nearFullCrossed,
            Guid.NewGuid(),
            80m,
            100m,
            80m);
    }

    private sealed class Fixture
    {
        public Fixture(TripCargoTransferRepositoryResult result)
        {
            Repository = new FakeRepository(result);
            Outbox = new FakeOutbox();
            UnitOfWork = new FakeUnitOfWork();
            Handler = new TransferCargoCommandHandler(
                Repository,
                Outbox,
                UnitOfWork,
                new FixedClock());
            Result = result;
        }

        public TripCargoTransferRepositoryResult Result { get; }
        public FakeRepository Repository { get; }
        public FakeOutbox Outbox { get; }
        public FakeUnitOfWork UnitOfWork { get; }
        public TransferCargoCommandHandler Handler { get; }

        public TransferCargoCommand Command(string targetState) =>
            new(
                Result.SourceTripId == Guid.Empty ? Guid.NewGuid() : Result.SourceTripId,
                Result.ParcelId == Guid.Empty ? Guid.NewGuid() : Result.ParcelId,
                Result.TargetTripId == Guid.Empty ? Guid.NewGuid() : Result.TargetTripId,
                targetState,
                AllowCapacityOverflow: true);
    }

    private sealed class FakeRepository(TripCargoTransferRepositoryResult result) : ITripRepository
    {
        public int CallCount { get; private set; }
        public bool? LastAllowCapacityOverflow { get; private set; }

        public Task<TripCargoTransferRepositoryResult> TransferCargoAsync(
            Guid sourceTripId,
            Guid parcelId,
            Guid targetTripId,
            string targetState,
            bool allowCapacityOverflow,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastAllowCapacityOverflow = allowCapacityOverflow;
            return Task.FromResult(result);
        }

        public Task<TripEntity?> GetByIdAsync(Guid id, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<TripEntity> AddAsync(TripEntity entity, CancellationToken ct) =>
            throw new NotSupportedException();

        public void Update(TripEntity entity) => throw new NotSupportedException();
        public void Remove(TripEntity entity) => throw new NotSupportedException();
        public IQueryable<TripEntity> Query() => throw new NotSupportedException();
        public IQueryable<TripEntity> QueryNoTracking() => throw new NotSupportedException();

        public Task<TripEntity?> GetWithSeatsAsync(
            Guid tripId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeOutbox : IIntegrationEventOutbox
    {
        public List<(Guid EventId, string EventType, string Payload)> Messages { get; } = [];

        public Task EnqueueAsync(
            Guid eventId,
            string eventType,
            string payloadJson,
            CancellationToken cancellationToken = default)
        {
            Messages.Add((eventId, eventType, payloadJson));
            return Task.CompletedTask;
        }

        public Task EnqueueAsync(
            string eventType,
            string payloadJson,
            CancellationToken cancellationToken = default) =>
            EnqueueAsync(Guid.NewGuid(), eventType, payloadJson, cancellationToken);
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public List<string> Calls { get; } = [];

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            Calls.Add("save");
            return Task.FromResult(1);
        }

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<Task<T>> operation,
            CancellationToken cancellationToken) =>
            operation();

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

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } =
            new(2026, 7, 30, 3, 0, 0, TimeSpan.Zero);
    }
}
