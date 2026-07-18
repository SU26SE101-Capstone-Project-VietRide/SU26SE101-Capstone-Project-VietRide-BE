using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Jobs;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Events;
using VietRide.Trip.Application.Features.DriverSchedules;
using VietRide.Trip.Domain.Entities;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Features.DriverSchedules;

public sealed class Day23AllPendingScheduleChangeProducerTests
{
    public static IEnumerable<object[]> SeverityCases =>
    [
        [IctDeparture(2026, 7, 20, 10, 0), IctDeparture(2026, 7, 20, 12, 0), "MINOR"],
        [IctDeparture(2026, 7, 20, 10, 0), IctDeparture(2026, 7, 20, 12, 0).AddTicks(1), "MEDIUM"],
        [IctDeparture(2026, 7, 20, 10, 0), IctDeparture(2026, 7, 20, 16, 0).AddTicks(-1), "MEDIUM"],
        [IctDeparture(2026, 7, 20, 10, 0), IctDeparture(2026, 7, 20, 16, 0), "MAJOR"],
        [IctDeparture(2026, 7, 20, 23, 45), IctDeparture(2026, 7, 21, 0, 15), "MAJOR"],
    ];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TwoHourBoundary_OnOldOrNewDeparture_IsAllowed(bool boundaryIsOldDeparture)
    {
        var fixture = Fixture.Create(maxClockReads: 1);
        var oldDeparture = boundaryIsOldDeparture
            ? fixture.Now.AddHours(2)
            : fixture.Now.AddHours(3);
        var requestedTime = boundaryIsOldDeparture
            ? new TimeOnly(20, 0)
            : new TimeOnly(19, 0);
        var trip = fixture.AddTrip(oldDeparture, confirmed: true);

        await fixture.Handler.Handle(fixture.Command(requestedTime), CancellationToken.None);

        fixture.UnitOfWork.Calls.Should().Equal("begin", "commit");
        trip.DepartureDateTime.Should().Be(boundaryIsOldDeparture
            ? fixture.Now.AddHours(3)
            : fixture.Now.AddHours(2));
        fixture.Clock.ReadCount.Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OneTickInsideTwoHourBoundary_OnOldOrNewDeparture_IsRejectedBeforeTransaction(
        bool boundaryIsOldDeparture)
    {
        var fixture = Fixture.Create(maxClockReads: 1);
        var oldDeparture = boundaryIsOldDeparture
            ? fixture.Now.AddHours(2).AddTicks(-1)
            : fixture.Now.AddHours(3);
        var requestedTime = boundaryIsOldDeparture
            ? new TimeOnly(20, 0)
            : TimeOnly.FromTimeSpan(TimeSpan.FromHours(19) - TimeSpan.FromTicks(1));
        var trip = fixture.AddTrip(oldDeparture, confirmed: true);

        var action = () => fixture.Handler.Handle(fixture.Command(requestedTime), CancellationToken.None);

        var error = await action.Should().ThrowAsync<CodedConflictException>();
        error.Which.ErrorCode.Should().Be("DRIVER_SCHEDULE_EDIT_TOO_LATE");
        trip.DepartureDateTime.Should().Be(oldDeparture);
        fixture.UnitOfWork.Calls.Should().BeEmpty();
        fixture.ScheduleAudits.Should().BeEmpty();
        fixture.TripAudits.Should().BeEmpty();
        fixture.Outbox.Items.Should().BeEmpty();
        fixture.Clock.ReadCount.Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DayRemovalWithDepartureChange_ChecksComputedNewDepartureBoundary(bool boundaryIsAllowed)
    {
        var fixture = Fixture.Create(maxClockReads: 1);
        var oldDeparture = fixture.Now.AddHours(3);
        var requestedTime = boundaryIsAllowed
            ? new TimeOnly(19, 0)
            : TimeOnly.FromTimeSpan(TimeSpan.FromHours(19) - TimeSpan.FromTicks(1));
        var trip = fixture.AddTrip(oldDeparture, confirmed: true);
        var command = fixture.Command(requestedTime, [4]);

        if (boundaryIsAllowed)
        {
            await fixture.Handler.Handle(command, CancellationToken.None);

            fixture.UnitOfWork.Calls.Should().Equal("begin", "commit");
            trip.Status.Should().Be(TripStatus.CANCELLED);
        }
        else
        {
            var action = () => fixture.Handler.Handle(command, CancellationToken.None);

            var error = await action.Should().ThrowAsync<CodedConflictException>();
            error.Which.ErrorCode.Should().Be("DRIVER_SCHEDULE_EDIT_TOO_LATE");
            fixture.UnitOfWork.Calls.Should().BeEmpty();
            trip.Status.Should().Be(TripStatus.SCHEDULED);
            fixture.ScheduleAudits.Should().BeEmpty();
            fixture.TripAudits.Should().BeEmpty();
            fixture.Outbox.Items.Should().BeEmpty();
        }

        fixture.Clock.ReadCount.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(SeverityCases))]
    public void Severity_UsesAllDeltaEdgesAndIctCalendarDate(
        DateTimeOffset oldDeparture,
        DateTimeOffset newDeparture,
        string expected)
    {
        TripScheduleChangedIntegrationEvent.ClassifySeverity(oldDeparture, newDeparture)
            .Should().Be(expected);
    }

    [Fact]
    public async Task PreflightFetchesWholeBatchBeforeRejectingNewDepartureWithoutWrites()
    {
        var fixture = Fixture.Create(maxClockReads: 1);
        var safeTrip = fixture.AddTrip(IctDeparture(2026, 7, 16, 20, 0), confirmed: true);
        var blockedTrip = fixture.AddTrip(IctDeparture(2026, 7, 15, 20, 0), confirmed: true);
        var blockedNewTime = TimeOnly.FromTimeSpan(TimeSpan.FromHours(19) - TimeSpan.FromTicks(1));

        var action = () => fixture.Handler.Handle(fixture.Command(blockedNewTime), CancellationToken.None);

        var error = await action.Should().ThrowAsync<CodedConflictException>();
        error.Which.ErrorCode.Should().Be("DRIVER_SCHEDULE_EDIT_TOO_LATE");
        fixture.Booking.RequestedTripIds.Should().BeEquivalentTo([safeTrip.Id, blockedTrip.Id]);
        fixture.UnitOfWork.Calls.Should().BeEmpty();
        fixture.ScheduleAudits.Should().BeEmpty();
        fixture.TripAudits.Should().BeEmpty();
        fixture.Outbox.Items.Should().BeEmpty();
        fixture.Schedule.DepartureTime.Should().Be(new TimeOnly(8, 0));
    }

    [Fact]
    public async Task SuccessfulCascadeStagesOneExactIdentityEvent_AndReplayNoOpStagesNone()
    {
        var fixture = Fixture.Create(maxClockReads: 2);
        var oldDeparture = IctDeparture(2026, 7, 16, 20, 0);
        var trip = fixture.AddTrip(oldDeparture, confirmed: true);
        var stop = TripStop.Create(
            trip.Id,
            Guid.NewGuid(),
            1,
            oldDeparture.AddHours(1),
            allowPickup: true,
            allowDropoff: false,
            distanceFromOriginKm: 10m);
        fixture.Stops.Items.Add(stop);
        var command = fixture.Command(new TimeOnly(21, 0));

        await fixture.Handler.Handle(command, CancellationToken.None);
        await fixture.Handler.Handle(command, CancellationToken.None);

        fixture.UnitOfWork.Calls.Should().Equal("begin", "commit");
        fixture.Outbox.Items.Should().ContainSingle();
        var staged = fixture.Outbox.Items.Single();
        staged.Type.Should().Be(TripScheduleChangedIntegrationEvent.EventTypeValue);
        using var payload = JsonDocument.Parse(staged.Payload);
        payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(staged.Id);
        payload.RootElement.GetProperty("tripId").GetGuid().Should().Be(trip.Id);
        payload.RootElement.GetProperty("operatorId").GetGuid().Should().Be(fixture.Schedule.OperatorId);
        payload.RootElement.GetProperty("oldDeparture").GetDateTimeOffset().Should().Be(oldDeparture);
        payload.RootElement.GetProperty("newDeparture").GetDateTimeOffset().Should().Be(oldDeparture.AddHours(1));
        payload.RootElement.GetProperty("severity").GetString().Should().Be("MINOR");
        stop.EstimatedArrivalTime.Should().Be(oldDeparture.AddHours(2));
        fixture.ScheduleAudits.Should().ContainSingle();
        fixture.TripAudits.Should().ContainSingle();
        fixture.Clock.ReadCount.Should().Be(2);
    }

    [Fact]
    public async Task SameValueNoOpStagesNoOutboxOrAuditRows()
    {
        var fixture = Fixture.Create(maxClockReads: 1);

        await fixture.Handler.Handle(fixture.Command(fixture.Schedule.DepartureTime), CancellationToken.None);

        fixture.UnitOfWork.Calls.Should().BeEmpty();
        fixture.Outbox.Items.Should().BeEmpty();
        fixture.ScheduleAudits.Should().BeEmpty();
        fixture.TripAudits.Should().BeEmpty();
        fixture.Booking.RequestedTripIds.Should().BeEmpty();
    }

    private static DateTimeOffset IctDeparture(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.FromHours(7));

    private sealed class Fixture
    {
        private Fixture(int maxClockReads)
        {
            Now = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
            var operatorId = Guid.NewGuid();
            Schedule = DriverSchedule.Create(
                operatorId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                assistantUserId: null,
                JsonSerializer.SerializeToElement(new[] { 3 }),
                new TimeOnly(8, 0),
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 12, 31),
                isActive: true);
            Schedules = new ScheduleRepository(Schedule);
            Trips = new TripRepository();
            Seats = new SeatRepository();
            Stops = new StopRepository();
            ScheduleAudits = [];
            TripAudits = [];
            Booking = new BookingClient();
            Outbox = new OutboxStub();
            UnitOfWork = new UnitOfWorkStub();
            Clock = new CountingClock(Now, maxClockReads);

            Handler = new UpdateDriverScheduleHandler(
                Schedules,
                new ScheduleAuditRepository(ScheduleAudits),
                Trips,
                Seats,
                Stops,
                new TripAuditRepository(TripAudits),
                Unexpected<IVehicleRepository>(),
                Unexpected<IRouteRepository>(),
                new AllowedIdentityClient(),
                Booking,
                Unexpected<ITripVehicleSwapService>(),
                Outbox,
                new JobScheduler(),
                UnitOfWork,
                Clock);
        }

        public DateTimeOffset Now { get; }

        public DriverSchedule Schedule { get; }

        public UpdateDriverScheduleHandler Handler { get; }

        public ScheduleRepository Schedules { get; }

        public TripRepository Trips { get; }

        public SeatRepository Seats { get; }

        public StopRepository Stops { get; }

        public List<DriverScheduleAuditLog> ScheduleAudits { get; }

        public List<TripAuditLog> TripAudits { get; }

        public BookingClient Booking { get; }

        public OutboxStub Outbox { get; }

        public UnitOfWorkStub UnitOfWork { get; }

        public CountingClock Clock { get; }

        public static Fixture Create(int maxClockReads) => new(maxClockReads);

        public TripEntity AddTrip(DateTimeOffset departure, bool confirmed)
        {
            var trip = TripEntity.Create(
                Schedule.OperatorId,
                Schedule.RouteId,
                Schedule.VehicleId!.Value,
                Schedule.DriverUserId,
                Schedule.AssistantUserId,
                Schedule.Id,
                departure,
                departure.AddHours(4),
                TripSource.AUTO_FROM_SCHEDULE,
                Money.FromRaw(100_000),
                maxCargoWeightKg: null,
                estimatedPassengerLuggageKg: 0m);
            Trips.Items.Add(trip);
            Booking.Projections[trip.Id] = confirmed
                ? new TripBookingImpactProjection(
                    trip.Id,
                    1,
                    [new TripBookingImpactProjection.ActiveBooking(Guid.NewGuid(), "CONFIRMED", [])])
                : new TripBookingImpactProjection(trip.Id, 0, []);
            return trip;
        }

        public UpdateDriverScheduleCommand Command(
            TimeOnly departureTime,
            IReadOnlyList<int>? dayOfWeek = null) =>
            new(
                Schedule.OperatorId,
                Schedule.Id,
                Guid.NewGuid(),
                "day23-request",
                UpdateDriverScheduleCommand.AllPending,
                DepartureTimeSpecified: true,
                DepartureTime: departureTime,
                DayOfWeekSpecified: dayOfWeek is not null,
                DayOfWeek: dayOfWeek,
                DriverUserIdSpecified: false,
                DriverUserId: null,
                AssistantUserIdSpecified: false,
                AssistantUserId: null,
                VehicleIdSpecified: false,
                VehicleId: null,
                ValidUntilSpecified: false,
                ValidUntil: null,
                IsActiveSpecified: false,
                IsActive: null);
    }

    private abstract class MemoryRepository<TEntity, TId>
        where TEntity : class
        where TId : notnull
    {
        protected MemoryRepository(List<TEntity>? items = null) => Items = items ?? [];

        public List<TEntity> Items { get; }

        public abstract Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken);

        public Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken)
        {
            Items.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(TEntity entity)
        {
        }

        public void Remove(TEntity entity) => Items.Remove(entity);

        public IQueryable<TEntity> Query() => Items.AsQueryable();

        public IQueryable<TEntity> QueryNoTracking() => Items.AsQueryable();
    }

    private sealed class ScheduleRepository : MemoryRepository<DriverSchedule, Guid>, IDriverScheduleRepository
    {
        public ScheduleRepository(DriverSchedule schedule)
            : base([schedule])
        {
        }

        public override Task<DriverSchedule?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.Id == id));

        public Task<DriverSchedule?> AcquireOwnedForUpdateAsync(
            Guid scheduleId,
            Guid operatorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.SingleOrDefault(item => item.Id == scheduleId && item.OperatorId == operatorId));

        public Task AcquireOverlapLocksAsync(
            Guid driverUserId,
            Guid? assistantUserId,
            Guid? vehicleId,
            IReadOnlyCollection<int> dayOfWeek,
            TimeOnly departureTime,
            DateOnly validFrom,
            DateOnly? validUntil,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> HasDriverConflictAsync(
            Guid driverUserId,
            IReadOnlyCollection<int> dayOfWeek,
            TimeOnly departureTime,
            DateOnly validFrom,
            DateOnly? validUntil,
            Guid? excludeScheduleId = null,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> HasAssistantConflictAsync(
            Guid assistantUserId,
            IReadOnlyCollection<int> dayOfWeek,
            TimeOnly departureTime,
            DateOnly validFrom,
            DateOnly? validUntil,
            Guid? excludeScheduleId = null,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> HasVehicleConflictAsync(
            Guid vehicleId,
            IReadOnlyCollection<int> dayOfWeek,
            TimeOnly departureTime,
            DateOnly validFrom,
            DateOnly? validUntil,
            Guid? excludeScheduleId = null,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class TripRepository : MemoryRepository<TripEntity, Guid>, ITripRepository
    {
        public override Task<TripEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.Id == id));

        public Task<TripEntity?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken) =>
            GetByIdAsync(tripId, cancellationToken);

        public Task<IReadOnlyList<TripEntity>> ListPendingByDriverScheduleAsync(
            Guid driverScheduleId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TripEntity>>(Items
                .Where(item => item.DriverScheduleId == driverScheduleId
                    && item.Status is TripStatus.SCHEDULED or TripStatus.BOARDING)
                .OrderBy(item => item.DepartureDateTime)
                .ThenBy(item => item.Id)
                .ToArray());

        public Task<TripEntity?> AcquireForVehicleSwapAsync(Guid tripId, CancellationToken cancellationToken) =>
            GetByIdAsync(tripId, cancellationToken);

        public Task<bool> HasVehicleConflictAsync(
            Guid vehicleId,
            DateTimeOffset departureDateTime,
            Guid excludedTripId,
            CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class SeatRepository : MemoryRepository<TripSeat, Guid>, ITripSeatRepository
    {
        public override Task<TripSeat?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.Id == id));

        public Task<IReadOnlyList<TripSeat>> AcquireForVehicleSwapAsync(
            Guid tripId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TripSeat>>(Items
                .Where(item => item.TripId == tripId)
                .OrderBy(item => item.SeatNumber)
                .ThenBy(item => item.Id)
                .ToArray());
    }

    private sealed class StopRepository : MemoryRepository<TripStop, (Guid TripId, Guid StopId)>, ITripStopRepository
    {
        public override Task<TripStop?> GetByIdAsync(
            (Guid TripId, Guid StopId) id,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.TripId == id.TripId && item.StopId == id.StopId));

        public Task<IReadOnlyList<TripStop>> AcquireByTripAsync(
            Guid tripId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TripStop>>(Items
                .Where(item => item.TripId == tripId)
                .OrderBy(item => item.OrderIndex)
                .ThenBy(item => item.StopId)
                .ToArray());
    }

    private sealed class ScheduleAuditRepository(List<DriverScheduleAuditLog> items)
        : IDriverScheduleAuditLogRepository
    {
        public Task AddAsync(DriverScheduleAuditLog auditLog, CancellationToken cancellationToken = default)
        {
            items.Add(auditLog);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DriverScheduleAuditLog>> ListByDriverScheduleIdAsync(
            Guid driverScheduleId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DriverScheduleAuditLog>>(items
                .Where(item => item.DriverScheduleId == driverScheduleId)
                .ToArray());
    }

    private sealed class TripAuditRepository(List<TripAuditLog> items) : ITripAuditLogRepository
    {
        public Task AddAsync(TripAuditLog auditLog, CancellationToken cancellationToken = default)
        {
            items.Add(auditLog);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TripAuditLog>> ListByTripIdAsync(
            Guid tripId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TripAuditLog>>(items
                .Where(item => item.TripId == tripId)
                .ToArray());
    }

    private sealed class AllowedIdentityClient : IIdentityInternalClient
    {
        public Task<OperatorWriteEligibilityValidation> ValidateOperatorCanWriteAsync(
            Guid operatorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperatorWriteEligibilityValidation.Allowed());

        public Task<IdentityUserLookupResult> GetUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No Identity user lookup is expected for a departure-only update.");
    }

    private sealed class BookingClient : IBookingImpactClient
    {
        public Dictionary<Guid, TripBookingImpactProjection> Projections { get; } = [];

        public List<Guid> RequestedTripIds { get; } = [];

        public Task<int> GetActiveBookingCountByStopAsync(
            Guid stopId,
            Guid operatorId,
            CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<TripBookingImpactProjection> GetTripEditImpactAsync(
            Guid tripId,
            Guid operatorId,
            CancellationToken cancellationToken)
        {
            RequestedTripIds.Add(tripId);
            return Task.FromResult(Projections[tripId]);
        }
    }

    private sealed class OutboxStub : IIntegrationEventOutbox
    {
        public List<(Guid Id, string Type, string Payload)> Items { get; } = [];

        public Task EnqueueAsync(
            string eventType,
            string payloadJson,
            CancellationToken cancellationToken = default) =>
            EnqueueAsync(Guid.NewGuid(), eventType, payloadJson, cancellationToken);

        public Task EnqueueAsync(
            Guid eventId,
            string eventType,
            string payloadJson,
            CancellationToken cancellationToken = default)
        {
            Items.Add((eventId, eventType, payloadJson));
            return Task.CompletedTask;
        }
    }

    private sealed class JobScheduler : ITripGenerationJobScheduler
    {
        public string EnqueueScheduleGeneration(Guid driverScheduleId) => driverScheduleId.ToString("N");
    }

    private sealed class UnitOfWorkStub : IUnitOfWork
    {
        public List<string> Calls { get; } = [];

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(1);

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<Task<T>> operation,
            CancellationToken cancellationToken) => throw new NotSupportedException();

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

    private sealed class CountingClock(DateTimeOffset now, int maxReads) : IClock
    {
        public int ReadCount { get; private set; }

        public DateTimeOffset UtcNow
        {
            get
            {
                ReadCount++;
                if (ReadCount > maxReads)
                {
                    throw new InvalidOperationException("The handler read its clock more than once.");
                }

                return now;
            }
        }
    }

    private static T Unexpected<T>()
        where T : class => DispatchProxy.Create<T, UnexpectedDependencyProxy>();

    public class UnexpectedDependencyProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException(
                $"Unexpected dependency call: {targetMethod?.DeclaringType?.Name}.{targetMethod?.Name}");
    }
}
