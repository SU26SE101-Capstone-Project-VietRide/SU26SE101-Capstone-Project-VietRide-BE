using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using VietRide.Shared.Application.Behaviors;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Jobs;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.DriverSchedules;
using VietRide.Trip.Application.Features.DriverTrips.GetAssignedTripRoute;
using VietRide.Trip.Application.Features.Vehicles;
using VietRide.Trip.Domain.Entities;
using RouteEntity = VietRide.Trip.Domain.Entities.Route;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Features.DriverSchedules;

public sealed class UpdateDriverScheduleHandlerTests
{
    [Fact]
    public void CanonicalAndAliasCommands_SkipAmbientTransactionBehavior()
    {
        typeof(UpdateDriverScheduleCommand).GetCustomAttribute<SkipTransactionAttribute>().Should().NotBeNull();
        typeof(UpdateDriverScheduleCrewCommand).GetCustomAttribute<SkipTransactionAttribute>().Should().NotBeNull();
    }

    [Fact]
    public async Task FutureOnly_AvoidsBooking_AndOwnsOneBeginOneCommitWithoutExplicitSave()
    {
        var fixture = Fixture.Create();
        var command = fixture.Command(
            UpdateDriverScheduleCommand.FutureOnly,
            departureTimeSpecified: true,
            departureTime: new TimeOnly(9, 0),
            driverUserIdSpecified: true,
            driverUserId: fixture.Schedule.DriverUserId);

        await fixture.Handler.Handle(command, CancellationToken.None);

        fixture.Booking.Calls.Should().Be(0);
        fixture.Identity.UserCalls.Should().Be(0, "same-value reference fields are not revalidated");
        fixture.UnitOfWork.Calls.Should().Equal("begin", "commit");
        fixture.Schedules.Calls.Should().ContainInOrder("schedule-lock", "overlap-lock", "overlap-check");
        fixture.Schedule.DepartureTime.Should().Be(new TimeOnly(9, 0));
        fixture.ScheduleAudits.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task FutureOnly_LockedReplacementVehicleIsRevalidatedBeforeScheduleMutation()
    {
        var fixture = Fixture.Create();
        var originalVehicleId = fixture.Schedule.VehicleId;
        var replacement = fixture.AddVehicle(Layout([Seat("1A", "STANDARD")]), "51B-FUTURE-LOCKED");
        fixture.Vehicles.OnAcquire = () => replacement.Deactivate();
        var command = fixture.Command(
            UpdateDriverScheduleCommand.FutureOnly,
            vehicleIdSpecified: true,
            vehicleId: replacement.Id);

        var action = () => fixture.Handler.Handle(command, CancellationToken.None);

        var error = await action.Should().ThrowAsync<ValidationException>();
        error.Which.Errors.Should().Contain(item => item.Field == "vehicleId");
        fixture.Schedule.VehicleId.Should().Be(originalVehicleId);
        fixture.ScheduleAudits.Items.Should().BeEmpty();
        fixture.UnitOfWork.Calls.Should().Equal("begin", "rollback");
    }

    [Fact]
    public async Task AllPendingNullVehicle_FailsBeforeIdentityBookingAndTransaction()
    {
        var fixture = Fixture.Create();
        var command = fixture.Command(
            UpdateDriverScheduleCommand.AllPending,
            vehicleIdSpecified: true,
            vehicleId: null);

        var action = () => fixture.Handler.Handle(command, CancellationToken.None);

        var error = await action.Should().ThrowAsync<ValidationException>();
        error.Which.Errors.Should().Contain(item => item.Field == "vehicleId");
        fixture.Identity.OperatorCalls.Should().Be(0);
        fixture.Identity.UserCalls.Should().Be(0);
        fixture.Booking.Calls.Should().Be(0);
        fixture.UnitOfWork.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task SameValueRequest_IsNoOpBeforeIdentityReferenceAndOverlapCalls()
    {
        var fixture = Fixture.Create();
        var command = fixture.Command(
            UpdateDriverScheduleCommand.AllPending,
            driverUserIdSpecified: true,
            driverUserId: fixture.Schedule.DriverUserId);

        await fixture.Handler.Handle(command, CancellationToken.None);

        fixture.Identity.OperatorCalls.Should().Be(0);
        fixture.Identity.UserCalls.Should().Be(0);
        fixture.Booking.Calls.Should().Be(0);
        fixture.Schedules.Calls.Should().BeEmpty();
        fixture.UnitOfWork.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task FutureOnly_BaseFareSetAndSameValueNoOp_DoNotMutateExistingTrip()
    {
        var fixture = Fixture.Create();
        var existingTrip = fixture.AddTrip(fixture.Now.AddDays(1));
        var set = fixture.Command(
            UpdateDriverScheduleCommand.FutureOnly,
            baseFareSpecified: true,
            baseFare: 400_000);

        var response = await fixture.Handler.Handle(set, CancellationToken.None);

        response.BaseFare.Should().Be(400_000);
        fixture.Schedule.BaseFare.Should().Be(Money.FromRaw(400_000));
        existingTrip.BaseFare.Amount.Should().Be(100_000);
        fixture.ScheduleAudits.Items.Should().ContainSingle();

        await fixture.Handler.Handle(set, CancellationToken.None);

        fixture.ScheduleAudits.Items.Should().ContainSingle();

        var clear = fixture.Command(
            UpdateDriverScheduleCommand.FutureOnly,
            baseFareSpecified: true,
            baseFare: null);
        await fixture.Handler.Handle(clear, CancellationToken.None);

        fixture.Schedule.BaseFare.Should().BeNull();
        existingTrip.BaseFare.Amount.Should().Be(100_000);
        fixture.ScheduleAudits.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task AllPending_BaseFareFailsBeforeBookingAndTransaction()
    {
        var fixture = Fixture.Create();
        var command = fixture.Command(
            UpdateDriverScheduleCommand.AllPending,
            baseFareSpecified: true,
            baseFare: 400_000);

        var action = () => fixture.Handler.Handle(command, CancellationToken.None);

        var error = await action.Should().ThrowAsync<ValidationException>();
        error.Which.Errors.Should().Contain(item => item.Field == "baseFare");
        fixture.Booking.Calls.Should().Be(0);
        fixture.UnitOfWork.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ExactTwoHourBoundary_WithConfirmedBooking_IsAllowed()
    {
        var fixture = Fixture.Create();
        var trip = fixture.AddTrip(fixture.Now.AddHours(2));
        fixture.Booking.Projections[trip.Id] = Projection(trip.Id, "CONFIRMED", []);
        var newDriver = Guid.NewGuid();
        fixture.Identity.Users[newDriver] = IdentityUserLookupResult.Success(
            newDriver, "DRIVER", fixture.Schedule.OperatorId, "ACTIVE");
        var command = fixture.Command(
            UpdateDriverScheduleCommand.AllPending,
            driverUserIdSpecified: true,
            driverUserId: newDriver);

        await fixture.Handler.Handle(command, CancellationToken.None);

        fixture.UnitOfWork.Calls.Should().Equal("begin", "commit");
        trip.DriverUserId.Should().Be(newDriver);
    }

    [Fact]
    public async Task LockedReplacementDriver_IsRejectedBeforeTransaction()
    {
        var fixture = Fixture.Create();
        var lockedDriver = Guid.NewGuid();
        fixture.Identity.Users[lockedDriver] = IdentityUserLookupResult.Success(
            lockedDriver,
            "DRIVER",
            fixture.Schedule.OperatorId,
            "LOCKED");
        var command = fixture.Command(
            UpdateDriverScheduleCommand.FutureOnly,
            driverUserIdSpecified: true,
            driverUserId: lockedDriver);

        var action = () => fixture.Handler.Handle(command, CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainSingle(error => error.Field == "driverUserId");
        fixture.Schedule.DriverUserId.Should().NotBe(lockedDriver);
        fixture.UnitOfWork.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task IncompatibleHeldSeat_PrecedesGeneralTwoHourTooLateConflict()
    {
        var fixture = Fixture.Create();
        var trip = fixture.AddTrip(fixture.Now.AddHours(1));
        fixture.Seats.Items.Add(TripSeat.Create(trip.Id, "A1", TripSeatType.STANDARD, TripSeatStatus.HELD));
        fixture.Booking.Projections[trip.Id] = Projection(trip.Id, "CONFIRMED", ["A1"]);
        var replacement = fixture.AddVehicle(Layout([]), "51B-REPLACE");
        var command = fixture.Command(
            UpdateDriverScheduleCommand.AllPending,
            vehicleIdSpecified: true,
            vehicleId: replacement.Id);

        var action = () => fixture.Handler.Handle(command, CancellationToken.None);

        var error = await action.Should().ThrowAsync<CodedConflictException>();
        error.Which.ErrorCode.Should().Be("TRIP_VEHICLE_SWAP_HELD_SEAT_CONFLICT");
        fixture.UnitOfWork.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task VehicleMatrix_ClassifiesAbsentDisabledDriverAreaLower_AndKeepsEqualHigherCompatible()
    {
        var fixture = Fixture.Create();
        var trip = fixture.AddTrip(fixture.Now.AddHours(10));
        var oldSeats = new[]
        {
            TripSeat.Create(trip.Id, "A", TripSeatType.STANDARD, TripSeatStatus.BOOKED),
            TripSeat.Create(trip.Id, "B", TripSeatType.STANDARD, TripSeatStatus.BOOKED),
            TripSeat.Create(trip.Id, "C", TripSeatType.STANDARD, TripSeatStatus.BOOKED),
            TripSeat.Create(trip.Id, "D", TripSeatType.VIP, TripSeatStatus.BOOKED),
            TripSeat.Create(trip.Id, "E", TripSeatType.SLEEPER_UPPER, TripSeatStatus.BOOKED),
            TripSeat.Create(trip.Id, "F", TripSeatType.STANDARD, TripSeatStatus.BOOKED),
        };
        fixture.Seats.Items.AddRange(oldSeats);
        fixture.Booking.Projections[trip.Id] = Projection(
            trip.Id,
            "CONFIRMED",
            oldSeats.Select(seat => seat.SeatNumber).ToArray());
        var replacement = fixture.AddVehicle(
            Layout([
                Seat("B", "STANDARD", disabled: true),
                Seat("C", "DRIVER_AREA"),
                Seat("D", "STANDARD"),
                Seat("E", "SLEEPER_UPPER"),
                Seat("F", "VIP"),
            ]),
            "51B-MATRIX");
        var command = fixture.Command(
            UpdateDriverScheduleCommand.AllPending,
            vehicleIdSpecified: true,
            vehicleId: replacement.Id);

        await fixture.Handler.Handle(command, CancellationToken.None);

        fixture.Swap.Impacts.SelectMany(impact => impact.SeatNumbers.Select(seat => (seat, impact.Reason)))
            .Should().BeEquivalentTo([
                ("A", VehicleSwapBookingSeatImpact.SeatRemoved),
                ("B", VehicleSwapBookingSeatImpact.SeatDisabled),
                ("C", VehicleSwapBookingSeatImpact.SeatDisabled),
                ("D", VehicleSwapBookingSeatImpact.SeatTypeDowngraded),
            ]);
        fixture.Swap.Impacts.SelectMany(impact => impact.SeatNumbers).Should().NotContain(["E", "F"]);
    }

    [Fact]
    public async Task LockedReplacementVehicle_IsRevalidatedBeforeCascade()
    {
        var fixture = Fixture.Create();
        var trip = fixture.AddTrip(fixture.Now.AddHours(10));
        fixture.Booking.Projections[trip.Id] = new TripBookingImpactProjection(trip.Id, 0, []);
        var replacement = fixture.AddVehicle(Layout([Seat("1A", "STANDARD")]), "51B-LOCKED");
        fixture.Vehicles.OnAcquire = () => replacement.Deactivate();
        var command = fixture.Command(
            UpdateDriverScheduleCommand.AllPending,
            vehicleIdSpecified: true,
            vehicleId: replacement.Id);

        var action = () => fixture.Handler.Handle(command, CancellationToken.None);

        var error = await action.Should().ThrowAsync<ValidationException>();
        error.Which.Errors.Should().Contain(item => item.Field == "vehicleId");
        fixture.Swap.Impacts.Should().BeEmpty();
        fixture.UnitOfWork.Calls.Should().Equal("begin", "rollback");
    }

    [Fact]
    public async Task AllPending_GeneratorCompletingBeforeScheduleLock_ForcesRetryWithoutSkippingNewTrip()
    {
        var fixture = Fixture.Create();
        var preflightTrip = fixture.AddTrip(fixture.Now.AddHours(8));
        var originalDepartureTime = fixture.Schedule.DepartureTime;
        fixture.Schedules.OnAcquire = () => fixture.AddTrip(fixture.Now.AddHours(10));
        var command = fixture.Command(
            UpdateDriverScheduleCommand.AllPending,
            departureTimeSpecified: true,
            departureTime: new TimeOnly(9, 0));

        var action = () => fixture.Handler.Handle(command, CancellationToken.None);

        var error = await action.Should().ThrowAsync<CodedConflictException>();
        error.Which.ErrorCode.Should().Be("TRIP_NOT_EDITABLE");
        fixture.Booking.Calls.Should().Be(1, "only the pre-lock snapshot may fetch Booking projections");
        fixture.Trips.Items.Should().HaveCount(2);
        preflightTrip.DepartureDateTime.Should().Be(fixture.Now.AddHours(8));
        fixture.Schedule.DepartureTime.Should().Be(originalDepartureTime);
        fixture.ScheduleAudits.Items.Should().BeEmpty();
        fixture.UnitOfWork.Calls.Should().Equal("begin", "rollback");
    }

    [Fact]
    public async Task DayRemovalCancelsOnlyRemovedDay_WhileValidUntilAndInactivePreserveGeneratedTrips()
    {
        var fixture = Fixture.Create(days: [1, 2]);
        var monday = fixture.AddTrip(new DateTimeOffset(2026, 7, 20, 1, 0, 0, TimeSpan.Zero));
        var tuesday = fixture.AddTrip(new DateTimeOffset(2026, 7, 21, 1, 0, 0, TimeSpan.Zero));
        var removeTuesday = fixture.Command(
            UpdateDriverScheduleCommand.AllPending,
            dayOfWeekSpecified: true,
            dayOfWeek: [1]);

        await fixture.Handler.Handle(removeTuesday, CancellationToken.None);

        monday.Status.Should().Be(TripStatus.SCHEDULED);
        tuesday.Status.Should().Be(TripStatus.CANCELLED);
        fixture.Outbox.Items.Should().Contain(item => item.Type == "trip.trip.cancelled");

        var preservation = Fixture.Create();
        var preserved = preservation.AddTrip(preservation.Now.AddDays(3));
        var originalDeparture = preserved.DepartureDateTime;
        var stop = TripStop.Create(preserved.Id, Guid.NewGuid(), 1, originalDeparture.AddHours(1), true, false, 10m);
        preservation.Stops.Items.Add(stop);
        var stopBaseline = stop.EstimatedArrivalTime;
        var command = preservation.Command(
            UpdateDriverScheduleCommand.AllPending,
            validUntilSpecified: true,
            validUntil: preservation.Schedule.ValidFrom.AddDays(5),
            isActiveSpecified: true,
            isActive: false);

        await preservation.Handler.Handle(command, CancellationToken.None);

        preserved.Status.Should().Be(TripStatus.SCHEDULED);
        preserved.DepartureDateTime.Should().Be(originalDeparture);
        stop.EstimatedArrivalTime.Should().Be(stopBaseline);
        preservation.TripAudits.Items.Should().BeEmpty();
        preservation.Outbox.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task FailureWhileStagingOutbox_RollsBackWithoutCommit()
    {
        var fixture = Fixture.Create(days: [1, 2]);
        fixture.AddTrip(new DateTimeOffset(2026, 7, 21, 1, 0, 0, TimeSpan.Zero));
        fixture.Outbox.ThrowOnEnqueue = true;
        var command = fixture.Command(
            UpdateDriverScheduleCommand.AllPending,
            dayOfWeekSpecified: true,
            dayOfWeek: [1]);

        var action = () => fixture.Handler.Handle(command, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
        fixture.UnitOfWork.Calls.Should().Equal("begin", "rollback");
        fixture.ScheduleAudits.Items.Should().ContainSingle();
        fixture.TripAudits.Items.Should().BeEmpty();
    }

    private static TripBookingImpactProjection Projection(Guid tripId, string status, IReadOnlyList<string> seats) =>
        new(tripId, 1, [new TripBookingImpactProjection.ActiveBooking(Guid.NewGuid(), status, seats)]);

    private static SeatLayoutSeatDto Seat(string number, string type, bool disabled = false) =>
        new(number, 1, 1, 1, type, false, false, disabled);

    private static SeatLayoutDto Layout(IReadOnlyList<SeatLayoutSeatDto> seats) =>
        new(1, "TEST", seats.Count, 1, Math.Max(1, seats.Count), 1, [], seats);

    private sealed class Fixture
    {
        private Fixture(DriverSchedule schedule, DateTimeOffset now)
        {
            Schedule = schedule;
            Now = now;
            Schedules = new ScheduleRepository(schedule);
            Trips = new TripRepository();
            Seats = new SeatRepository();
            Stops = new StopRepository();
            Vehicles = new VehicleRepository();
            Routes = new RouteRepository();
            Identity = new IdentityClient();
            Booking = new BookingClient();
            Swap = new SwapService();
            ScheduleAudits = new ScheduleAuditRepository();
            TripAudits = new TripAuditRepository();
            Outbox = new OutboxStub();
            Jobs = new JobScheduler();
            UnitOfWork = new UnitOfWorkStub();
            Clock = new FixedClock(now);

            Identity.Users[schedule.DriverUserId] = IdentityUserLookupResult.Success(
                schedule.DriverUserId, "DRIVER", schedule.OperatorId, "ACTIVE");
            if (schedule.AssistantUserId.HasValue)
            {
                Identity.Users[schedule.AssistantUserId.Value] = IdentityUserLookupResult.Success(
                    schedule.AssistantUserId.Value, "ASSISTANT", schedule.OperatorId, "ACTIVE");
            }

            var vehicle = AddVehicle(Layout([Seat("1A", "STANDARD")]), "51B-ORIGINAL", schedule.VehicleId);
            vehicle.Id.Should().Be(schedule.VehicleId!.Value);

            Handler = new UpdateDriverScheduleHandler(
                Schedules,
                ScheduleAudits,
                Trips,
                Seats,
                Stops,
                TripAudits,
                Vehicles,
                Routes,
                Identity,
                Booking,
                Swap,
                Outbox,
                Jobs,
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
        public VehicleRepository Vehicles { get; }
        public RouteRepository Routes { get; }
        public IdentityClient Identity { get; }
        public BookingClient Booking { get; }
        public SwapService Swap { get; }
        public ScheduleAuditRepository ScheduleAudits { get; }
        public TripAuditRepository TripAudits { get; }
        public OutboxStub Outbox { get; }
        public JobScheduler Jobs { get; }
        public UnitOfWorkStub UnitOfWork { get; }
        public FixedClock Clock { get; }

        public static Fixture Create(IReadOnlyCollection<int>? days = null)
        {
            var operatorId = Guid.NewGuid();
            var vehicleId = Guid.NewGuid();
            var schedule = DriverSchedule.Create(
                operatorId,
                Guid.NewGuid(),
                vehicleId,
                Guid.NewGuid(),
                null,
                JsonSerializer.SerializeToElement((days ?? [1, 3, 5]).ToArray()),
                new TimeOnly(8, 0),
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 12, 31),
                true);
            return new Fixture(schedule, new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero));
        }

        public TripEntity AddTrip(DateTimeOffset departure)
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
                null,
                0m);
            Trips.Items.Add(trip);
            Booking.Projections[trip.Id] = new TripBookingImpactProjection(trip.Id, 0, []);
            return trip;
        }

        public Vehicle AddVehicle(SeatLayoutDto layout, string plate, Guid? id = null)
        {
            var vehicle = Vehicle.Create(
                Schedule.OperatorId,
                Guid.NewGuid(),
                plate,
                JsonSerializer.SerializeToElement(layout),
                Math.Max(1, layout.TotalSeats),
                null,
                null);
            if (id.HasValue)
            {
                typeof(BaseEntity<Guid>).GetProperty(nameof(BaseEntity<Guid>.Id))!
                    .SetValue(vehicle, id.Value);
            }

            Vehicles.Items.Add(vehicle);
            return vehicle;
        }

        public UpdateDriverScheduleCommand Command(
            string applyTo,
            bool departureTimeSpecified = false,
            TimeOnly? departureTime = null,
            bool dayOfWeekSpecified = false,
            IReadOnlyList<int>? dayOfWeek = null,
            bool driverUserIdSpecified = false,
            Guid? driverUserId = null,
            bool assistantUserIdSpecified = false,
            Guid? assistantUserId = null,
            bool vehicleIdSpecified = false,
            Guid? vehicleId = null,
            bool validUntilSpecified = false,
            DateOnly? validUntil = null,
            bool isActiveSpecified = false,
            bool? isActive = null,
            bool baseFareSpecified = false,
            long? baseFare = null) =>
            new(
                Schedule.OperatorId,
                Schedule.Id,
                Guid.NewGuid(),
                "request-1",
                applyTo,
                departureTimeSpecified,
                departureTime,
                dayOfWeekSpecified,
                dayOfWeek,
                driverUserIdSpecified,
                driverUserId,
                assistantUserIdSpecified,
                assistantUserId,
                vehicleIdSpecified,
                vehicleId,
                validUntilSpecified,
                validUntil,
                isActiveSpecified,
                isActive,
                baseFareSpecified,
                baseFare);
    }

    private abstract class MemoryRepository<TEntity, TId>
        where TEntity : class
        where TId : notnull
    {
        protected MemoryRepository(List<TEntity>? items = null) => Items = items ?? [];
        public List<TEntity> Items { get; }
        public abstract Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken);
        public Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken) { Items.Add(entity); return Task.FromResult(entity); }
        public void Update(TEntity entity) { }
        public void Remove(TEntity entity) => Items.Remove(entity);
        public IQueryable<TEntity> Query() => Items.AsQueryable();
        public IQueryable<TEntity> QueryNoTracking() => Items.AsQueryable();
    }

    private sealed class ScheduleRepository : MemoryRepository<DriverSchedule, Guid>, IDriverScheduleRepository
    {
        public ScheduleRepository(DriverSchedule schedule) : base([schedule]) { }
        public List<string> Calls { get; } = [];
        public Action? OnAcquire { get; set; }
        public override Task<DriverSchedule?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.Id == id));
        public Task<DriverSchedule?> AcquireOwnedForUpdateAsync(Guid id, Guid operatorId, CancellationToken cancellationToken = default)
        {
            Calls.Add("schedule-lock");
            OnAcquire?.Invoke();
            return Task.FromResult(Items.SingleOrDefault(item => item.Id == id && item.OperatorId == operatorId));
        }
        public Task AcquireOverlapLocksAsync(Guid driverUserId, Guid? assistantUserId, Guid? vehicleId, IReadOnlyCollection<int> dayOfWeek, TimeOnly departureTime, DateOnly validFrom, DateOnly? validUntil, CancellationToken cancellationToken = default)
        {
            Calls.Add("overlap-lock");
            return Task.CompletedTask;
        }
        public Task<bool> HasDriverConflictAsync(Guid driverUserId, IReadOnlyCollection<int> dayOfWeek, TimeOnly departureTime, DateOnly validFrom, DateOnly? validUntil, Guid? excludeScheduleId = null, CancellationToken cancellationToken = default)
        {
            Calls.Add("overlap-check");
            return Task.FromResult(false);
        }
        public Task<bool> HasAssistantConflictAsync(Guid assistantUserId, IReadOnlyCollection<int> dayOfWeek, TimeOnly departureTime, DateOnly validFrom, DateOnly? validUntil, Guid? excludeScheduleId = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> HasVehicleConflictAsync(Guid vehicleId, IReadOnlyCollection<int> dayOfWeek, TimeOnly departureTime, DateOnly validFrom, DateOnly? validUntil, Guid? excludeScheduleId = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class TripRepository : MemoryRepository<TripEntity, Guid>, ITripRepository
    {
        public override Task<TripEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.SingleOrDefault(item => item.Id == id));
        public Task<TripEntity?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken) => GetByIdAsync(tripId, cancellationToken);
        public Task<IReadOnlyList<TripEntity>> ListPendingByDriverScheduleAsync(Guid scheduleId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TripEntity>>(Items.Where(item => item.DriverScheduleId == scheduleId && item.Status is TripStatus.SCHEDULED or TripStatus.BOARDING).OrderBy(item => item.DepartureDateTime).ThenBy(item => item.Id).ToArray());
        public Task<TripEntity?> AcquireForVehicleSwapAsync(Guid tripId, CancellationToken cancellationToken) => GetByIdAsync(tripId, cancellationToken);
        public Task<bool> HasVehicleConflictAsync(Guid vehicleId, DateTimeOffset departureDateTime, Guid excludedTripId, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class SeatRepository : MemoryRepository<TripSeat, Guid>, ITripSeatRepository
    {
        public override Task<TripSeat?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.SingleOrDefault(item => item.Id == id));
        public Task<IReadOnlyList<TripSeat>> AcquireForVehicleSwapAsync(Guid tripId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TripSeat>>(Items.Where(item => item.TripId == tripId).OrderBy(item => item.SeatNumber).ThenBy(item => item.Id).ToArray());
    }

    private sealed class StopRepository : MemoryRepository<TripStop, (Guid TripId, Guid StopId)>, ITripStopRepository
    {
        public override Task<TripStop?> GetByIdAsync((Guid TripId, Guid StopId) id, CancellationToken cancellationToken) => Task.FromResult(Items.SingleOrDefault(item => item.TripId == id.TripId && item.StopId == id.StopId));
        public Task<IReadOnlyList<TripStop>> AcquireByTripAsync(Guid tripId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TripStop>>(Items.Where(item => item.TripId == tripId).OrderBy(item => item.OrderIndex).ThenBy(item => item.StopId).ToArray());
    }

    private sealed class VehicleRepository : MemoryRepository<Vehicle, Guid>, IVehicleRepository
    {
        public Action? OnAcquire { get; set; }
        public override Task<Vehicle?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.SingleOrDefault(item => item.Id == id));
        public Task<Vehicle?> GetOwnedByIdAsync(Guid operatorId, Guid vehicleId, CancellationToken cancellationToken) => Task.FromResult(Items.SingleOrDefault(item => item.Id == vehicleId && item.OperatorId == operatorId));
        public Task<IReadOnlyList<Vehicle>> AcquireForVehicleSwapAsync(Guid operatorId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
        {
            OnAcquire?.Invoke();
            return Task.FromResult<IReadOnlyList<Vehicle>>(Items.Where(item => item.OperatorId == operatorId && ids.Contains(item.Id)).OrderBy(item => item.Id).ToArray());
        }
        public Task<PagedResult<Vehicle>> ListByOperatorAsync(Guid operatorId, int page, int pageSize, string? search, string? searchIn, string? sortBy, string sortDir, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> LicensePlateExistsAsync(string licensePlate, Guid? excludedVehicleId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> TryAddAsync(Vehicle vehicle, CancellationToken cancellationToken) { Items.Add(vehicle); return Task.FromResult(true); }
        public Task<bool> TryUpdateAsync(Vehicle vehicle, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class RouteRepository : MemoryRepository<RouteEntity, Guid>, IRouteRepository
    {
        public override Task<RouteEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.SingleOrDefault(item => item.Id == id));
        public Task<RouteEntity?> GetOwnedByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken) => Task.FromResult(Items.SingleOrDefault(item => item.Id == routeId && item.OperatorId == operatorId));
        public Task<RouteEntity?> GetOwnedActiveByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken) => GetOwnedByIdAsync(operatorId, routeId, cancellationToken);
        public Task<IReadOnlyList<RouteEntity>> ListByOperatorAsync(Guid operatorId, string? search, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RouteEntity>>(Items.Where(item => item.OperatorId == operatorId).ToArray());
        public Task<bool> ExistsActiveOwnedByOperatorAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken) => Task.FromResult(Items.Any(item => item.OperatorId == operatorId && item.Id == routeId));
    }

    private sealed class IdentityClient : IIdentityInternalClient
    {
        public int OperatorCalls { get; private set; }
        public int UserCalls { get; private set; }
        public Dictionary<Guid, IdentityUserLookupResult> Users { get; } = [];
        public Task<OperatorWriteEligibilityValidation> ValidateOperatorCanWriteAsync(Guid operatorId, CancellationToken cancellationToken = default)
        {
            OperatorCalls++;
            return Task.FromResult(OperatorWriteEligibilityValidation.Allowed());
        }
        public Task<IdentityUserLookupResult> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            UserCalls++;
            return Task.FromResult(Users.GetValueOrDefault(userId) ?? IdentityUserLookupResult.ValidationFailure("missing"));
        }
    }

    private sealed class BookingClient : IBookingImpactClient
    {
        public int Calls { get; private set; }
        public Dictionary<Guid, TripBookingImpactProjection> Projections { get; } = [];
        public Task<int> GetActiveBookingCountByStopAsync(Guid stopId, Guid operatorId, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<TripBookingImpactProjection> GetTripEditImpactAsync(Guid tripId, Guid operatorId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Projections.GetValueOrDefault(tripId) ?? new TripBookingImpactProjection(tripId, 0, []));
        }
    }

    private sealed class SwapService : ITripVehicleSwapService
    {
        public IReadOnlyCollection<VehicleSwapBookingSeatImpact> Impacts { get; private set; } = [];
        public Task<bool> StageSwapAsync(TripEntity trip, Vehicle oldVehicle, Vehicle newVehicle, IReadOnlyCollection<TripSeat> lockedSeats, IReadOnlyCollection<VehicleSwapBookingSeatImpact> bookingSeatImpacts, Guid actorUserId, string auditAction, string requestId, DateTimeOffset occurredAt, CancellationToken cancellationToken)
        {
            Impacts = bookingSeatImpacts;
            trip.ChangeVehicle(newVehicle.Id);
            return Task.FromResult(true);
        }
    }

    private sealed class ScheduleAuditRepository : IDriverScheduleAuditLogRepository
    {
        public List<DriverScheduleAuditLog> Items { get; } = [];
        public Task AddAsync(DriverScheduleAuditLog auditLog, CancellationToken cancellationToken = default) { Items.Add(auditLog); return Task.CompletedTask; }
        public Task<IReadOnlyList<DriverScheduleAuditLog>> ListByDriverScheduleIdAsync(Guid driverScheduleId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DriverScheduleAuditLog>>(Items.Where(item => item.DriverScheduleId == driverScheduleId).ToArray());
    }

    private sealed class TripAuditRepository : ITripAuditLogRepository
    {
        public List<TripAuditLog> Items { get; } = [];
        public Task AddAsync(TripAuditLog auditLog, CancellationToken cancellationToken = default) { Items.Add(auditLog); return Task.CompletedTask; }
        public Task<IReadOnlyList<TripAuditLog>> ListByTripIdAsync(Guid tripId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TripAuditLog>>(Items.Where(item => item.TripId == tripId).ToArray());
    }

    private sealed class OutboxStub : IIntegrationEventOutbox
    {
        public List<(Guid Id, string Type, string Payload)> Items { get; } = [];
        public bool ThrowOnEnqueue { get; set; }
        public Task EnqueueAsync(string eventType, string payloadJson, CancellationToken cancellationToken = default)
            => EnqueueAsync(Guid.NewGuid(), eventType, payloadJson, cancellationToken);

        public Task EnqueueAsync(
            Guid eventId,
            string eventType,
            string payloadJson,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnEnqueue) throw new InvalidOperationException("outbox failure");
            Items.Add((eventId, eventType, payloadJson));
            return Task.CompletedTask;
        }
    }

    private sealed class JobScheduler : ITripGenerationJobScheduler
    {
        public List<Guid> Items { get; } = [];
        public string EnqueueScheduleGeneration(Guid driverScheduleId) { Items.Add(driverScheduleId); return Guid.NewGuid().ToString("N"); }
    }

    private sealed class UnitOfWorkStub : IUnitOfWork
    {
        public List<string> Calls { get; } = [];
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) { Calls.Add("save"); return Task.FromResult(1); }
        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task BeginTransactionAsync(CancellationToken cancellationToken) { Calls.Add("begin"); return Task.CompletedTask; }
        public Task CommitAsync(CancellationToken cancellationToken) { Calls.Add("commit"); return Task.CompletedTask; }
        public Task RollbackAsync(CancellationToken cancellationToken) { Calls.Add("rollback"); return Task.CompletedTask; }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
