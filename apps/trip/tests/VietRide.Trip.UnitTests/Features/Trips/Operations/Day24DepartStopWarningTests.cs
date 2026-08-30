using System.Text.Json;
using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Trips.Operations;
using VietRide.Trip.Domain.Entities;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Features.Trips.Operations;

public sealed class Day24DepartStopWarningTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 19, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_PositiveCount_PersistsDepartureAndEnqueuesFrozenEventIdentity()
    {
        var fixture = CreateFixture();
        fixture.Booking.Projection = new(
            fixture.Trip.Id,
            fixture.Stop.Id,
            2);

        var result = await fixture.Sut.Handle(fixture.Command, CancellationToken.None);

        result.Should().Be(new DepartStopResponse(
            fixture.Trip.Id,
            fixture.Stop.Id,
            Now,
            2,
            true));
        fixture.TripStops.MarkDepartedCalls.Should().Be(1);
        fixture.Outbox.Entries.Should().HaveCount(2);
        fixture.Outbox.Entries.Should().ContainSingle(row => row.EventType == "trip.stop.departed");
        var entry = fixture.Outbox.Entries.Single(row => row.EventType == "trip.stop.departed_with_pending");
        entry.EventType.Should().Be("trip.stop.departed_with_pending");
        entry.EventId.Should().NotBeEmpty();
        using var document = JsonDocument.Parse(entry.Payload);
        var root = document.RootElement;
        root.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            [
                "eventId", "occurredAt", "eventType", "tripId", "stopId", "stopName",
                "pendingPassengerCount", "driverUserId", "assistantUserId", "departedAt",
            ]);
        root.GetProperty("eventId").GetGuid().Should().Be(entry.EventId);
        root.GetProperty("eventType").GetString().Should().Be(entry.EventType);
        root.GetProperty("tripId").GetGuid().Should().Be(fixture.Trip.Id);
        root.GetProperty("stopId").GetGuid().Should().Be(fixture.Stop.Id);
        root.GetProperty("stopName").GetString().Should().Be(fixture.Stop.Name);
        root.GetProperty("pendingPassengerCount").GetInt32().Should().Be(2);
        root.GetProperty("driverUserId").GetGuid().Should().Be(fixture.Trip.DriverUserId);
        root.GetProperty("assistantUserId").GetGuid().Should().Be(fixture.Trip.AssistantUserId!.Value);
        root.GetProperty("departedAt").GetDateTimeOffset().Should().Be(Now);
    }

    [Fact]
    public async Task Handle_ZeroCount_PersistsDepartureAndEmitsOperationalDepartureOnly()
    {
        var fixture = CreateFixture();
        fixture.Booking.Projection = new(fixture.Trip.Id, fixture.Stop.Id, 0);

        var result = await fixture.Sut.Handle(fixture.Command, CancellationToken.None);

        result.PendingPassengerCount.Should().Be(0);
        result.EventEmitted.Should().BeFalse();
        fixture.TripStops.MarkDepartedCalls.Should().Be(1);
        var entry = fixture.Outbox.Entries.Should().ContainSingle().Subject;
        entry.EventType.Should().Be("trip.stop.departed");
        using var document = JsonDocument.Parse(entry.Payload);
        document.RootElement.GetProperty("tripId").GetGuid().Should().Be(fixture.Trip.Id);
        document.RootElement.GetProperty("stopId").GetGuid().Should().Be(fixture.Stop.Id);
        document.RootElement.GetProperty("operatorId").GetGuid().Should().Be(fixture.Trip.OperatorId);
        document.RootElement.GetProperty("departedAt").GetDateTimeOffset().Should().Be(Now);
    }

    [Theory]
    [InlineData(TripStatus.SCHEDULED)]
    [InlineData(TripStatus.BOARDING)]
    [InlineData(TripStatus.COMPLETED)]
    [InlineData(TripStatus.CANCELLED)]
    [InlineData(TripStatus.DISRUPTED)]
    public async Task Handle_TripOutsideInProgress_ReturnsTripNotInProgressBeforeBooking(
        TripStatus status)
    {
        var fixture = CreateFixture(status);

        var act = () => fixture.Sut.Handle(fixture.Command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("TRIP_NOT_IN_PROGRESS");
        fixture.Booking.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(TripStopStatus.PENDING)]
    [InlineData(TripStopStatus.SKIPPED)]
    public async Task Handle_StopNotArrived_ReturnsExactValidationBeforeBooking(
        TripStopStatus status)
    {
        var fixture = CreateFixture(stopStatus: status);

        var act = () => fixture.Sut.Handle(fixture.Command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("TRIP_STOP_NOT_ARRIVED");
        fixture.Booking.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Handle_AlreadyDeparted_ReturnsConflictBeforeBooking()
    {
        var fixture = CreateFixture();
        typeof(TripStop).GetProperty(nameof(TripStop.ActualDepartureTime))!
            .SetValue(fixture.TripStop, Now.AddMinutes(-1));

        var act = () => fixture.Sut.Handle(fixture.Command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("TRIP_STOP_ALREADY_DEPARTED");
        fixture.Booking.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("crew")]
    [InlineData("tenant")]
    [InlineData("role")]
    public async Task Handle_AssignmentRoleOrTenantMismatch_ReturnsForbiddenBeforeBooking(
        string mismatch)
    {
        var fixture = CreateFixture();
        var command = fixture.Command with
        {
            ActorUserId = mismatch == "crew" ? Guid.NewGuid() : fixture.Command.ActorUserId,
            ActorRole = mismatch == "role" ? "PASSENGER" : fixture.Command.ActorRole,
            OperatorId = mismatch == "tenant" ? Guid.NewGuid() : fixture.Command.OperatorId,
        };

        var act = () => fixture.Sut.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ForbiddenException>();
        exception.Which.ErrorCode.Should().Be("FORBIDDEN");
        fixture.Booking.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_BookingTransportOrTimeout_MapsToUpstreamUnavailableWithoutEvent(
        bool timeout)
    {
        var fixture = CreateFixture();
        fixture.Booking.Exception = timeout
            ? new TaskCanceledException("timeout")
            : new HttpRequestException("5xx");

        var act = () => fixture.Sut.Handle(fixture.Command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<TripUpstreamUnavailableException>();
        exception.Which.ErrorCode.Should().Be("UPSTREAM_UNAVAILABLE");
        exception.Which.StatusCode.Should().Be(502);
        fixture.Outbox.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CasRaceLoser_ReturnsAlreadyDepartedWithoutBookingOrEvent()
    {
        var fixture = CreateFixture(casWinner: false);

        var act = () => fixture.Sut.Handle(fixture.Command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("TRIP_STOP_ALREADY_DEPARTED");
        fixture.Booking.Calls.Should().Be(0);
        fixture.Outbox.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UnresolvedParcelWithoutApprovedOverride_BlocksBeforeDeparture()
    {
        var fixture = CreateFixture();
        var unresolvedParcelId = Guid.NewGuid();
        fixture.Parcel.Clearance = new(
            fixture.Trip.Id,
            fixture.Stop.Id,
            fixture.Trip.OperatorId,
            "BLOCKED_PENDING_APPROVAL",
            [unresolvedParcelId],
            Guid.NewGuid(),
            null,
            null);

        var action = () => fixture.Sut.Handle(fixture.Command, CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("PARCEL_STOP_RECONCILIATION_REQUIRED");
        fixture.TripStops.MarkDepartedCalls.Should().Be(0);
        fixture.Booking.Calls.Should().Be(0);
        fixture.Outbox.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ApprovedParcelOverride_AllowsDeparture()
    {
        var fixture = CreateFixture();
        fixture.Parcel.Clearance = new(
            fixture.Trip.Id,
            fixture.Stop.Id,
            fixture.Trip.OperatorId,
            "APPROVED_OVERRIDE",
            [Guid.NewGuid()],
            Guid.NewGuid(),
            Guid.NewGuid(),
            Now.AddMinutes(-1));

        var result = await fixture.Sut.Handle(fixture.Command, CancellationToken.None);

        result.StopId.Should().Be(fixture.Stop.Id);
        fixture.TripStops.MarkDepartedCalls.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_ParcelClearanceTransportOrTimeout_FailsClosedBeforeDeparture(
        bool timeout)
    {
        var fixture = CreateFixture();
        fixture.Parcel.Exception = timeout
            ? new TaskCanceledException("timeout")
            : new HttpRequestException("5xx");

        var action = () => fixture.Sut.Handle(fixture.Command, CancellationToken.None);

        var exception = await action.Should().ThrowAsync<TripUpstreamUnavailableException>();
        exception.Which.ErrorCode.Should().Be("UPSTREAM_UNAVAILABLE");
        exception.Which.StatusCode.Should().Be(502);
        fixture.TripStops.MarkDepartedCalls.Should().Be(0);
        fixture.Booking.Calls.Should().Be(0);
        fixture.Outbox.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_InconsistentParcelClearance_FailsClosedBeforeDeparture()
    {
        var fixture = CreateFixture();
        fixture.Parcel.Clearance = new(
            fixture.Trip.Id,
            fixture.Stop.Id,
            fixture.Trip.OperatorId,
            "CLEAR",
            [Guid.NewGuid()],
            null,
            null,
            null);

        var action = () => fixture.Sut.Handle(fixture.Command, CancellationToken.None);

        var exception = await action.Should().ThrowAsync<TripUpstreamUnavailableException>();
        exception.Which.ErrorCode.Should().Be("UPSTREAM_UNAVAILABLE");
        fixture.TripStops.MarkDepartedCalls.Should().Be(0);
        fixture.Booking.Calls.Should().Be(0);
        fixture.Outbox.Entries.Should().BeEmpty();
    }

    private static Fixture CreateFixture(
        TripStatus tripStatus = TripStatus.IN_PROGRESS,
        TripStopStatus stopStatus = TripStopStatus.ARRIVED,
        bool casWinner = true)
    {
        var operatorId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var assistantId = Guid.NewGuid();
        var trip = TripEntity.Create(
            operatorId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            driverId,
            assistantId,
            null,
            Now.AddHours(-2),
            Now.AddHours(2),
            TripSource.AUTO_FROM_SCHEDULE,
            Money.FromRaw(100_000),
            null,
            0);
        MoveTripToStatus(trip, tripStatus);
        var stop = Stop.Create(operatorId, "Bến xe Miền Đông Mới", 10, 106);
        var tripStop = TripStop.Create(trip.Id, stop.Id, 1, Now, true, true, 5);
        if (stopStatus == TripStopStatus.ARRIVED)
        {
            tripStop.MarkArrived(Now.AddMinutes(-5));
        }
        else if (stopStatus == TripStopStatus.SKIPPED)
        {
            tripStop.MarkSkipped();
        }

        var trips = new FakeTripRepository(trip);
        var tripStops = new FakeTripStopRepository(tripStop, casWinner);
        var stops = new FakeStopRepository(stop);
        var booking = new FakeBookingImpactClient();
        var parcel = new FakeParcelImpactClient();
        var outbox = new RecordingOutbox();
        var sut = new DepartStopHandler(
            trips,
            tripStops,
            stops,
            booking,
            parcel,
            outbox,
            new FrozenClock(Now));
        var command = new DepartStopCommand(
            trip.Id,
            stop.Id,
            driverId,
            "DRIVER",
            operatorId);
        return new Fixture(sut, command, trip, tripStop, stop, tripStops, booking, parcel, outbox);
    }

    private static void MoveTripToStatus(TripEntity trip, TripStatus status)
    {
        if (status == TripStatus.SCHEDULED)
        {
            return;
        }

        trip.MarkBoarding(Now.AddHours(-2));
        if (status == TripStatus.BOARDING)
        {
            return;
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
    }

    private sealed class FakeTripRepository(TripEntity trip) : ITripRepository
    {
        public Task<TripEntity?> GetForUpdateAsync(Guid tripId, CancellationToken cancellationToken)
            => Task.FromResult<TripEntity?>(trip.Id == tripId ? trip : null);
        public Task<TripEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<TripEntity?>(trip.Id == id ? trip : null);
        public Task<TripEntity> AddAsync(TripEntity entity, CancellationToken ct = default)
            => Task.FromResult(entity);
        public void Update(TripEntity entity) { }
        public void Remove(TripEntity entity) { }
        public IQueryable<TripEntity> Query() => new[] { trip }.AsQueryable();
        public IQueryable<TripEntity> QueryNoTracking() => Query();
        public Task<TripEntity?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken)
            => GetByIdAsync(tripId, cancellationToken);
    }

    private sealed class FakeTripStopRepository(TripStop tripStop, bool casWinner)
        : ITripStopRepository
    {
        public int MarkDepartedCalls { get; private set; }
        public Task<TripStop?> GetForUpdateAsync(
            Guid tripId, Guid stopId, CancellationToken cancellationToken)
            => Task.FromResult<TripStop?>(
                tripStop.TripId == tripId && tripStop.StopId == stopId ? tripStop : null);
        public Task<bool> TryMarkDepartedAsync(
            Guid tripId, Guid stopId, DateTimeOffset departedAt, CancellationToken cancellationToken)
        {
            MarkDepartedCalls++;
            return Task.FromResult(casWinner);
        }
        public Task<TripStop?> GetByIdAsync(
            (Guid TripId, Guid StopId) id, CancellationToken ct = default)
            => GetForUpdateAsync(id.TripId, id.StopId, ct);
        public Task<TripStop> AddAsync(TripStop entity, CancellationToken ct = default)
            => Task.FromResult(entity);
        public void Update(TripStop entity) { }
        public void Remove(TripStop entity) { }
        public IQueryable<TripStop> Query() => new[] { tripStop }.AsQueryable();
        public IQueryable<TripStop> QueryNoTracking() => Query();
    }

    private sealed class FakeStopRepository(Stop stop) : IStopRepository
    {
        public Task<Stop?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<Stop?>(stop.Id == id ? stop : null);
        public Task<Stop> AddAsync(Stop entity, CancellationToken ct = default)
            => Task.FromResult(entity);
        public void Update(Stop entity) { }
        public void Remove(Stop entity) { }
        public IQueryable<Stop> Query() => new[] { stop }.AsQueryable();
        public IQueryable<Stop> QueryNoTracking() => Query();
    }

    private sealed class FakeBookingImpactClient : IBookingImpactClient
    {
        public int Calls { get; private set; }
        public TripStopPendingPassengerCountProjection? Projection { get; set; }
        public Exception? Exception { get; set; }

        public Task<TripStopPendingPassengerCountProjection> GetPendingPassengerCountAsync(
            Guid tripId, Guid stopId, Guid operatorId, CancellationToken cancellationToken)
        {
            Calls++;
            if (Exception is not null)
            {
                return Task.FromException<TripStopPendingPassengerCountProjection>(Exception);
            }

            return Task.FromResult(Projection
                ?? new TripStopPendingPassengerCountProjection(tripId, stopId, 0));
        }

        public Task<TripBookingImpactProjection> GetTripEditImpactAsync(
            Guid tripId, Guid operatorId, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class FakeParcelImpactClient : IParcelImpactClient
    {
        public Exception? Exception { get; set; }
        public ParcelStopDepartureClearanceProjection Clearance { get; set; }
            = new(Guid.Empty, Guid.Empty, Guid.Empty, "CLEAR", [], null, null, null);

        public Task<ParcelStopDepartureClearanceProjection> GetStopDepartureClearanceAsync(
            Guid tripId,
            Guid stopId,
            Guid operatorId,
            CancellationToken cancellationToken)
        {
            if (Exception is not null)
                return Task.FromException<ParcelStopDepartureClearanceProjection>(Exception);
            return Task.FromResult(Clearance with
            {
                TripId = tripId,
                StopId = stopId,
                OperatorId = operatorId,
            });
        }

        public Task<TripParcelCancellationImpactProjection> GetTripCancellationImpactAsync(
            Guid tripId,
            Guid operatorId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ParcelTripCompletionClearanceProjection> GetTripCompletionClearanceAsync(
            Guid tripId,
            Guid operatorId,
            CancellationToken cancellationToken)
            => Task.FromResult(new ParcelTripCompletionClearanceProjection(
                tripId,
                operatorId,
                "CLEAR",
                [],
                []));
    }

    private sealed class RecordingOutbox : IIntegrationEventOutbox
    {
        public List<OutboxEntry> Entries { get; } = [];
        public Task EnqueueAsync(
            Guid eventId, string eventType, string payloadJson, CancellationToken ct = default)
        {
            Entries.Add(new OutboxEntry(eventId, eventType, payloadJson));
            return Task.CompletedTask;
        }
        public Task EnqueueAsync(
            string eventType, string payloadJson, CancellationToken ct = default)
            => EnqueueAsync(Guid.NewGuid(), eventType, payloadJson, ct);
    }

    private sealed class FrozenClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed record OutboxEntry(Guid EventId, string EventType, string Payload);

    private sealed record Fixture(
        DepartStopHandler Sut,
        DepartStopCommand Command,
        TripEntity Trip,
        TripStop TripStop,
        Stop Stop,
        FakeTripStopRepository TripStops,
        FakeBookingImpactClient Booking,
        FakeParcelImpactClient Parcel,
        RecordingOutbox Outbox);
}
