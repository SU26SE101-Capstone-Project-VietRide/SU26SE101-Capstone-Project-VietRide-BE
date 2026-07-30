using System.Text.Json;
using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Trips.Operations;
using VietRide.Trip.Domain.Entities;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Features.Trips.Operations;

public sealed class DisruptNoSubstitutionCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_InProgressOwnedTrip_PersistsCanonicalDisruptionWithoutTripWideRatio()
    {
        var trip = CreateTrip(TripStatus.IN_PROGRESS);
        var fixture = new Fixture(trip);

        var result = await fixture.Handler.Handle(
            new DisruptNoSubstitutionCommand(
                trip.Id,
                trip.OperatorId,
                Guid.NewGuid(),
                "  Road closure  "),
            CancellationToken.None);

        result.Should().Be(new DisruptNoSubstitutionResponse(
            trip.Id,
            "DISRUPTED",
            Now,
            false,
            "Road closure"));
        trip.Status.Should().Be(TripStatus.DISRUPTED);
        trip.DisruptedAt.Should().Be(Now);
        trip.HasSubstitution.Should().BeFalse();
        trip.DisruptionReason.Should().Be("Road closure");
        fixture.UnitOfWork.CommitCount.Should().Be(1);
        fixture.Outbox.Events.Should().ContainSingle();

        var recorded = fixture.Outbox.Events.Single();
        recorded.EventType.Should().Be("trip.trip.disrupted");
        using var document = JsonDocument.Parse(recorded.Payload);
        var payload = document.RootElement;
        payload.GetProperty("eventId").GetGuid().Should().Be(recorded.EventId);
        payload.GetProperty("occurredAt").GetDateTime().Should().Be(Now.UtcDateTime);
        payload.GetProperty("tripId").GetGuid().Should().Be(trip.Id);
        payload.GetProperty("operatorId").GetGuid().Should().Be(trip.OperatorId);
        payload.GetProperty("terminalAt").GetDateTimeOffset().Should().Be(Now);
        payload.GetProperty("hasSubstitution").GetBoolean().Should().BeFalse();
        payload.GetProperty("reason").GetString().Should().Be("Road closure");
        payload.TryGetProperty("traveledRatio", out _).Should().BeFalse();
        payload.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "eventId",
            "occurredAt",
            "tripId",
            "operatorId",
            "terminalAt",
            "hasSubstitution",
            "reason");
    }

    [Theory]
    [InlineData(TripStatus.SCHEDULED)]
    [InlineData(TripStatus.BOARDING)]
    public async Task Handle_PreDepartureTrip_ThrowsExactValidationCode(TripStatus status)
    {
        var trip = CreateTrip(status);
        var fixture = new Fixture(trip);

        var action = () => fixture.Handler.Handle(
            new DisruptNoSubstitutionCommand(
                trip.Id,
                trip.OperatorId,
                Guid.NewGuid(),
                "No replacement"),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedValidationException>()).Which;
        exception.ErrorCode.Should().Be("TRIP_NOT_IN_PROGRESS");
        trip.Status.Should().Be(status);
        fixture.Outbox.Events.Should().BeEmpty();
        fixture.UnitOfWork.RollbackCount.Should().Be(1);
    }

    [Theory]
    [InlineData(TripStatus.COMPLETED)]
    [InlineData(TripStatus.CANCELLED)]
    [InlineData(TripStatus.DISRUPTED)]
    public async Task Handle_TerminalTrip_ThrowsExactConflictCode(TripStatus status)
    {
        var trip = CreateTrip(status);
        var fixture = new Fixture(trip);

        var action = () => fixture.Handler.Handle(
            new DisruptNoSubstitutionCommand(
                trip.Id,
                trip.OperatorId,
                Guid.NewGuid(),
                "No replacement"),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedConflictException>()).Which;
        exception.ErrorCode.Should().Be("TRIP_ALREADY_TERMINAL");
        fixture.Outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CrossTenantTrip_IsHiddenAsNotFound()
    {
        var trip = CreateTrip(TripStatus.IN_PROGRESS);
        var fixture = new Fixture(trip);

        var action = () => fixture.Handler.Handle(
            new DisruptNoSubstitutionCommand(
                trip.Id,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "No replacement"),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedNotFoundException>()).Which;
        exception.ErrorCode.Should().Be("TRIP_NOT_FOUND");
        trip.Status.Should().Be(TripStatus.IN_PROGRESS);
        fixture.Outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_MissingTrip_ThrowsExactNotFoundCode()
    {
        var fixture = new Fixture(null);

        var action = () => fixture.Handler.Handle(
            new DisruptNoSubstitutionCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "No replacement"),
            CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedNotFoundException>()).Which;
        exception.ErrorCode.Should().Be("TRIP_NOT_FOUND");
        fixture.Outbox.Events.Should().BeEmpty();
        fixture.UnitOfWork.RollbackCount.Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validator_BlankReason_IsRejected(string reason)
    {
        var validator = new DisruptNoSubstitutionCommandValidator();

        var result = validator.Validate(new DisruptNoSubstitutionCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            reason));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.ErrorCode == "VALIDATION_ERROR");
    }

    [Fact]
    public void Validator_TrimmedReasonOver500Characters_IsRejected()
    {
        var validator = new DisruptNoSubstitutionCommandValidator();

        var result = validator.Validate(new DisruptNoSubstitutionCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            $" {new string('x', 501)} "));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.ErrorCode == "VALIDATION_ERROR");
    }

    private static TripEntity CreateTrip(TripStatus status)
    {
        var trip = TripEntity.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            Now.AddHours(-2),
            Now.AddHours(2),
            TripSource.MANUAL,
            Money.FromRaw(100_000),
            1_000m,
            100m);

        if (status == TripStatus.SCHEDULED)
        {
            return trip;
        }

        trip.MarkBoarding(Now.AddHours(-2));
        if (status == TripStatus.BOARDING)
        {
            return trip;
        }

        trip.Start(Now.AddHours(-1));
        if (status == TripStatus.COMPLETED)
        {
            trip.CompleteManually(Now, trip.DriverUserId);
        }
        else if (status == TripStatus.CANCELLED)
        {
            trip.Cancel(Now, trip.DriverUserId, "cancelled");
        }
        else if (status == TripStatus.DISRUPTED)
        {
            trip.Disrupt(Now, "disrupted");
        }

        return trip;
    }

    private sealed class Fixture
    {
        public Fixture(TripEntity? trip)
        {
            Outbox = new RecordingOutbox();
            UnitOfWork = new RecordingUnitOfWork();
            Handler = new DisruptNoSubstitutionCommandHandler(
                new FakeTripRepository(trip),
                Outbox,
                UnitOfWork,
                new FrozenClock(Now));
        }

        public RecordingOutbox Outbox { get; }
        public RecordingUnitOfWork UnitOfWork { get; }
        public DisruptNoSubstitutionCommandHandler Handler { get; }
    }

    private sealed class FakeTripRepository(TripEntity? trip) : ITripRepository
    {
        public Task<TripEntity?> GetForUpdateAsync(Guid tripId, CancellationToken cancellationToken)
            => Task.FromResult(trip?.Id == tripId ? trip : null);

        public Task<TripEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => GetForUpdateAsync(id, cancellationToken);

        public Task<TripEntity> AddAsync(TripEntity entity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Update(TripEntity entity) => throw new NotSupportedException();
        public void Remove(TripEntity entity) => throw new NotSupportedException();
        public IQueryable<TripEntity> Query() =>
            trip is null ? Array.Empty<TripEntity>().AsQueryable() : new[] { trip }.AsQueryable();
        public IQueryable<TripEntity> QueryNoTracking() => Query();
        public Task<TripEntity?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken)
            => GetForUpdateAsync(tripId, cancellationToken);
    }

    private sealed class RecordingOutbox : IIntegrationEventOutbox
    {
        public List<(Guid EventId, string EventType, string Payload)> Events { get; } = [];

        public Task EnqueueAsync(
            Guid eventId,
            string eventType,
            string payloadJson,
            CancellationToken ct = default)
        {
            Events.Add((eventId, eventType, payloadJson));
            return Task.CompletedTask;
        }

        public Task EnqueueAsync(string eventType, string payloadJson, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public int CommitCount { get; private set; }
        public int RollbackCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(1);
        public Task BeginTransactionAsync(CancellationToken ct) => Task.CompletedTask;

        public Task CommitAsync(CancellationToken ct)
        {
            CommitCount++;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken ct)
        {
            RollbackCount++;
            return Task.CompletedTask;
        }

        public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct)
            => await operation();
    }

    private sealed class FrozenClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
