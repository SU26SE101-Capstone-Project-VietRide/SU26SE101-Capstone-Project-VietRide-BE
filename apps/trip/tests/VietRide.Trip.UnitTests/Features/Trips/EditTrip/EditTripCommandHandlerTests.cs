using System.Text.Json;
using FluentAssertions;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.Trips.EditTrip;
using VietRide.Trip.Application.Features.Trips.GetTripDetail;
using VietRide.Trip.Application.Features.Vehicles;
using VietRide.Trip.Domain.Entities;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Features.Trips.EditTrip;

public sealed class EditTripCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CombinedRouteAndFareChange_RebuildsManualFareForSharedAndNewStops()
    {
        var fixture = new Fixture();
        var sharedStopId = Guid.NewGuid();
        var removedStopId = Guid.NewGuid();
        var addedStopId = Guid.NewGuid();
        fixture.Stops.Items.AddRange([
            TripStop.Create(fixture.Trip.Id, sharedStopId, 1, Now.AddHours(1), true, true, 10m),
            TripStop.Create(fixture.Trip.Id, removedStopId, 2, Now.AddHours(2), true, true, 20m),
        ]);
        fixture.Fares.Items.AddRange([
            TripStopFare.Create(fixture.Trip.Id, sharedStopId, Money.FromRaw(310_000), TripStopFareSource.MANUAL_OVERRIDE),
            TripStopFare.Create(fixture.Trip.Id, removedStopId, Money.FromRaw(320_000), TripStopFareSource.MANUAL_OVERRIDE),
        ]);
        fixture.RouteStops.Items.AddRange([
            RouteStop.Create(fixture.NewRoute.Id, sharedStopId, 1, 45, 12m),
            RouteStop.Create(fixture.NewRoute.Id, addedStopId, 2, 90, 30m),
        ]);

        await fixture.Handler.Handle(fixture.Command(baseFare: 450_001, routeId: fixture.NewRoute.Id), default);

        fixture.Trip.RouteId.Should().Be(fixture.NewRoute.Id);
        fixture.Trip.BaseFare.Amount.Should().Be(450_001);
        fixture.Stops.Items.Select(stop => stop.StopId).Should().BeEquivalentTo([sharedStopId, addedStopId]);
        fixture.Fares.Items.Should().HaveCount(2);
        fixture.Fares.Items.Select(fare => fare.StopId).Should().BeEquivalentTo([sharedStopId, addedStopId]);
        fixture.Fares.Items.Should().OnlyContain(fare =>
            fare.Source == TripStopFareSource.MANUAL_OVERRIDE && fare.FareFromThisStop.Amount == 450_001);
        fixture.Audits.Items.Should().HaveCount(2);
        fixture.Outbox.Items.Should().ContainSingle(item => item.EventType == "trip.trip.route_changed");
        fixture.UnitOfWork.SaveCount.Should().Be(1);
        fixture.UnitOfWork.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task BoardingVehicleSwap_WithIncompatibleHeldSeat_ReturnsTooLateBeforeTransaction()
    {
        var fixture = new Fixture();
        fixture.Trip.MarkBoarding(Now);
        fixture.Seats.Items.Add(TripSeat.Create(
            fixture.Trip.Id,
            "A01",
            TripSeatType.STANDARD,
            TripSeatStatus.HELD));
        var newVehicle = CreateVehicle(fixture.OperatorId, "51B-999.99", []);
        fixture.Vehicles.Items.Add(newVehicle);

        var action = () => fixture.Handler.Handle(fixture.Command(vehicleId: newVehicle.Id), default);

        var exception = await action.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("TRIP_VEHICLE_SWAP_TOO_LATE");
        fixture.UnitOfWork.BeginCount.Should().Be(0);
        fixture.Swap.StageCount.Should().Be(0);
    }

    [Fact]
    public async Task LockedRevalidation_WhenConcurrentWriterAlreadyAppliedRequest_CommitsNoOpWithoutSideEffects()
    {
        var fixture = new Fixture();
        fixture.Trips.OnAcquire = trip => trip.UpdateNotes("updated by contender");

        var detail = await fixture.Handler.Handle(fixture.Command(notes: "updated by contender"), default);

        detail.Notes.Should().Be("updated by contender");
        fixture.UnitOfWork.BeginCount.Should().Be(1);
        fixture.UnitOfWork.SaveCount.Should().Be(0);
        fixture.UnitOfWork.CommitCount.Should().Be(1);
        fixture.UnitOfWork.RollbackCount.Should().Be(0);
        fixture.Audits.Items.Should().BeEmpty();
        fixture.Outbox.Items.Should().BeEmpty();
        fixture.Swap.StageCount.Should().Be(0);
    }

    [Fact]
    public async Task LockedRevalidation_WhenScalarDiverged_RollsBackInsteadOfOverwriting()
    {
        var fixture = new Fixture();
        fixture.Trips.OnAcquire = trip => trip.UpdateNotes("contender value");

        var action = () => fixture.Handler.Handle(fixture.Command(notes: "requested value"), default);

        var exception = await action.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("TRIP_NOT_EDITABLE");
        fixture.Trip.Notes.Should().Be("contender value");
        fixture.UnitOfWork.SaveCount.Should().Be(0);
        fixture.UnitOfWork.CommitCount.Should().Be(0);
        fixture.UnitOfWork.RollbackCount.Should().Be(1);
        fixture.Audits.Items.Should().BeEmpty();
        fixture.Outbox.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task VehicleSwap_AllowsRecoveryFromInactiveOldVehicle_WhenNewVehicleIsActive()
    {
        var fixture = new Fixture();
        fixture.Vehicles.Items.Single(vehicle => vehicle.Id == fixture.Trip.VehicleId)
            .ChangeStatus(VehicleStatus.MAINTENANCE);
        var replacement = CreateVehicle(
            fixture.OperatorId,
            "51B-222.22",
            [new SeatLayoutItem("A01", "STANDARD", false)]);
        fixture.Vehicles.Items.Add(replacement);

        await fixture.Handler.Handle(fixture.Command(vehicleId: replacement.Id), default);

        fixture.Swap.StageCount.Should().Be(1);
        fixture.UnitOfWork.CommitCount.Should().Be(1);
    }

    public static TheoryData<string, string?, bool, string?> CompatibilityCases()
    {
        var cases = new TheoryData<string, string?, bool, string?>
        {
            { "STANDARD", null, false, VehicleSwapBookingSeatImpact.SeatRemoved },
            { "STANDARD", "STANDARD", true, VehicleSwapBookingSeatImpact.SeatDisabled },
            { "STANDARD", "DRIVER_AREA", false, VehicleSwapBookingSeatImpact.SeatDisabled },
        };
        var passengerTypes = new[] { "STANDARD", "SLEEPER_UPPER", "SLEEPER_LOWER", "VIP" };
        for (var oldRank = 0; oldRank < passengerTypes.Length; oldRank++)
        {
            for (var newRank = 0; newRank < passengerTypes.Length; newRank++)
            {
                cases.Add(
                    passengerTypes[oldRank],
                    passengerTypes[newRank],
                    false,
                    newRank < oldRank ? VehicleSwapBookingSeatImpact.SeatTypeDowngraded : null);
            }
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(CompatibilityCases))]
    public async Task VehicleSwap_ClassifiesCompatibilityMatrix(
        string oldType,
        string? newType,
        bool disabled,
        string? expectedReason)
    {
        var fixture = new Fixture();
        fixture.Seats.Items.Add(TripSeat.Create(
            fixture.Trip.Id,
            " a01 ",
            Enum.Parse<TripSeatType>(oldType),
            TripSeatStatus.BOOKED));
        var replacement = CreateVehicle(
            fixture.OperatorId,
            "51B-333.33",
            newType is null ? [] : [new SeatLayoutItem(" A01 ", newType, disabled)]);
        fixture.Vehicles.Items.Add(replacement);
        var bookingId = Guid.NewGuid();
        fixture.Booking.Projection = new TripBookingImpactProjection(
            fixture.Trip.Id,
            1,
            [new TripBookingImpactProjection.ActiveBooking(bookingId, "CONFIRMED", [" a01 "])]);

        await fixture.Handler.Handle(fixture.Command(vehicleId: replacement.Id), default);

        fixture.Swap.StageCount.Should().Be(1);
        if (expectedReason is null)
        {
            fixture.Swap.LastImpacts.Should().BeEmpty();
        }
        else
        {
            fixture.Swap.LastImpacts.Should().ContainSingle()
                .Which.Should().BeEquivalentTo(new
                {
                    BookingId = bookingId,
                    SeatNumbers = new[] { "A01" },
                    Reason = expectedReason,
                });
        }
    }

    public static TheoryData<string, string, int, string?> VehicleConflictBoundaryCases() => new()
    {
        { "SCHEDULED", "HELD", 600, "TRIP_VEHICLE_SWAP_HELD_SEAT_CONFLICT" },
        { "SCHEDULED", "BOOKED", 29, "TRIP_VEHICLE_SWAP_TOO_LATE" },
        { "SCHEDULED", "BOOKED", 30, "TRIP_VEHICLE_SWAP_TOO_LATE" },
        { "SCHEDULED", "BOOKED", 31, null },
        { "BOARDING", "HELD", 600, "TRIP_VEHICLE_SWAP_TOO_LATE" },
        { "BOARDING", "BOOKED", 600, "TRIP_VEHICLE_SWAP_TOO_LATE" },
    };

    [Theory]
    [MemberData(nameof(VehicleConflictBoundaryCases))]
    public async Task VehicleSwap_EnforcesHeldBookedLifecycleAndStrictDeadline(
        string tripStatus,
        string seatStatus,
        int departureMinutes,
        string? expectedError)
    {
        var fixture = new Fixture(TimeSpan.FromMinutes(departureMinutes));
        if (tripStatus == "BOARDING")
        {
            fixture.Trip.MarkBoarding(Now);
        }

        fixture.Seats.Items.Add(TripSeat.Create(
            fixture.Trip.Id,
            "A01",
            TripSeatType.VIP,
            Enum.Parse<TripSeatStatus>(seatStatus)));
        var replacement = CreateVehicle(
            fixture.OperatorId,
            "51B-444.44",
            [new SeatLayoutItem("A01", "STANDARD", false)]);
        fixture.Vehicles.Items.Add(replacement);

        var action = () => fixture.Handler.Handle(fixture.Command(vehicleId: replacement.Id), default);

        if (expectedError is null)
        {
            await action.Should().NotThrowAsync();
            fixture.Swap.StageCount.Should().Be(1);
        }
        else
        {
            var exception = await action.Should().ThrowAsync<CodedConflictException>();
            exception.Which.ErrorCode.Should().Be(expectedError);
            fixture.UnitOfWork.BeginCount.Should().Be(0);
            fixture.Swap.StageCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task CombinedRouteVehicleChange_UsesRouteBookingConflictPrecedence()
    {
        var fixture = new Fixture();
        fixture.Booking.Projection = new TripBookingImpactProjection(fixture.Trip.Id, 1, []);
        fixture.Seats.Items.Add(TripSeat.Create(
            fixture.Trip.Id,
            "A01",
            TripSeatType.VIP,
            TripSeatStatus.HELD));
        var replacement = CreateVehicle(fixture.OperatorId, "51B-555.55", []);
        fixture.Vehicles.Items.Add(replacement);

        var action = () => fixture.Handler.Handle(
            fixture.Command(routeId: fixture.NewRoute.Id, vehicleId: replacement.Id),
            default);

        var exception = await action.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("TRIP_ROUTE_CHANGE_BOOKINGS_EXIST");
        fixture.Booking.CallCount.Should().Be(1);
        fixture.UnitOfWork.BeginCount.Should().Be(0);
    }

    [Fact]
    public async Task VehicleSwap_PreflightsBookingExactlyOnceBeforeTransactionAndService()
    {
        var fixture = new Fixture();
        var replacement = CreateVehicle(
            fixture.OperatorId,
            "51B-666.66",
            [new SeatLayoutItem("A01", "STANDARD", false)]);
        fixture.Vehicles.Items.Add(replacement);

        await fixture.Handler.Handle(fixture.Command(vehicleId: replacement.Id), default);

        fixture.Booking.CallCount.Should().Be(1);
        fixture.Calls.Should().ContainInOrder("booking-preflight", "begin", "swap", "save", "commit");
        fixture.Calls.Count(call => call == "booking-preflight").Should().Be(1);
    }

    [Fact]
    public async Task TenantMismatch_IsMaskedBeforeBookingPreflightOrTransaction()
    {
        var fixture = new Fixture();

        var action = () => fixture.Handler.Handle(
            fixture.Command(notes: "masked") with { OperatorId = Guid.NewGuid() },
            default);

        var exception = await action.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be("TRIP_NOT_FOUND");
        fixture.Booking.CallCount.Should().Be(0);
        fixture.UnitOfWork.BeginCount.Should().Be(0);
    }

    [Fact]
    public async Task ActualNoOp_ReturnsWithoutBookingTransactionAuditOrOutbox()
    {
        var fixture = new Fixture();

        await fixture.Handler.Handle(fixture.Command(baseFare: fixture.Trip.BaseFare.Amount), default);

        fixture.Booking.CallCount.Should().Be(0);
        fixture.UnitOfWork.BeginCount.Should().Be(0);
        fixture.Audits.Items.Should().BeEmpty();
        fixture.Outbox.Items.Should().BeEmpty();
        fixture.Swap.StageCount.Should().Be(0);
    }

    [Fact]
    public async Task LockedRevalidation_RejectsNewVehicleThatBecameInactive()
    {
        var fixture = new Fixture();
        var replacement = CreateVehicle(fixture.OperatorId, "51B-777.77", []);
        fixture.Vehicles.Items.Add(replacement);
        fixture.Vehicles.OnAcquire = vehicles =>
            vehicles.Single(vehicle => vehicle.Id == replacement.Id).Deactivate();

        var action = () => fixture.Handler.Handle(fixture.Command(vehicleId: replacement.Id), default);

        var exception = await action.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("VEHICLE_NOT_ACTIVE");
        fixture.UnitOfWork.RollbackCount.Should().Be(1);
        fixture.Swap.StageCount.Should().Be(0);
    }

    [Fact]
    public async Task LockedRevalidation_RejectsVehicleConflictThatAppearedAfterPreflight()
    {
        var fixture = new Fixture();
        var replacement = CreateVehicle(fixture.OperatorId, "51B-888.88", []);
        fixture.Vehicles.Items.Add(replacement);
        fixture.Trips.VehicleConflicts.Enqueue(false);
        fixture.Trips.VehicleConflicts.Enqueue(true);

        var action = () => fixture.Handler.Handle(fixture.Command(vehicleId: replacement.Id), default);

        var exception = await action.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("TRIP_VEHICLE_CONFLICT");
        fixture.Trips.VehicleConflictChecks.Should().Be(2);
        fixture.Calls.Should().ContainInOrder(
            "booking-preflight",
            "vehicle-conflict-preflight",
            "begin",
            "trip-lock",
            "vehicle-conflict-serialized",
            "rollback");
        fixture.UnitOfWork.RollbackCount.Should().Be(1);
        fixture.Swap.StageCount.Should().Be(0);
    }

    [Fact]
    public async Task LockedRevalidation_WhenHeldSeatBecameBookedAndIncompatible_RejectsStaleEditWithoutSideEffects()
    {
        var fixture = new Fixture();
        fixture.Seats.Items.Add(TripSeat.Create(
            fixture.Trip.Id,
            "A01",
            TripSeatType.STANDARD,
            TripSeatStatus.HELD));
        var replacement = CreateVehicle(
            fixture.OperatorId,
            "51B-889.89",
            [new SeatLayoutItem("A01", "STANDARD", false)]);
        fixture.Vehicles.Items.Add(replacement);
        fixture.Seats.OnAcquire = seats =>
        {
            seats.Clear();
            var racedSeat = TripSeat.Create(
                fixture.Trip.Id,
                "A01",
                TripSeatType.VIP,
                TripSeatStatus.HELD);
            racedSeat.MarkBooked();
            seats.Add(racedSeat);
        };

        var action = () => fixture.Handler.Handle(
            fixture.Command(routeId: fixture.NewRoute.Id, vehicleId: replacement.Id),
            default);

        var exception = await action.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("TRIP_NOT_EDITABLE");
        fixture.Booking.CallCount.Should().Be(1);
        fixture.UnitOfWork.RollbackCount.Should().Be(1);
        fixture.UnitOfWork.SaveCount.Should().Be(0);
        fixture.UnitOfWork.CommitCount.Should().Be(0);
        fixture.Swap.StageCount.Should().Be(0);
        fixture.Audits.Items.Should().BeEmpty();
        fixture.Outbox.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveFailure_RollsBackCombinedAuditAndOutboxStaging()
    {
        var fixture = new Fixture();
        fixture.UnitOfWork.ThrowOnSave = true;
        fixture.UnitOfWork.OnRollback = () =>
        {
            fixture.Audits.Items.Clear();
            fixture.Outbox.Items.Clear();
        };

        var action = () => fixture.Handler.Handle(
            fixture.Command(baseFare: 450_001, routeId: fixture.NewRoute.Id),
            default);

        await action.Should().ThrowAsync<InvalidOperationException>();
        fixture.UnitOfWork.SaveCount.Should().Be(1);
        fixture.UnitOfWork.CommitCount.Should().Be(0);
        fixture.UnitOfWork.RollbackCount.Should().Be(1);
        fixture.Audits.Items.Should().BeEmpty();
        fixture.Outbox.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task RouteOnlyChange_RebuildsStopsAndClearsObsoleteFareOverrides()
    {
        var fixture = new Fixture();
        var oldStopId = Guid.NewGuid();
        var newStopId = Guid.NewGuid();
        fixture.Stops.Items.Add(TripStop.Create(
            fixture.Trip.Id,
            oldStopId,
            1,
            Now.AddHours(1),
            true,
            true,
            10m));
        fixture.Fares.Items.Add(TripStopFare.Create(
            fixture.Trip.Id,
            oldStopId,
            Money.FromRaw(350_000),
            TripStopFareSource.MANUAL_OVERRIDE));
        fixture.RouteStops.Items.Add(RouteStop.Create(fixture.NewRoute.Id, newStopId, 1, 90, 20m));

        await fixture.Handler.Handle(fixture.Command(routeId: fixture.NewRoute.Id), default);

        fixture.Stops.Items.Should().ContainSingle(stop => stop.StopId == newStopId);
        fixture.Fares.Items.Should().BeEmpty();
        fixture.Trip.EstimatedArrivalTime.Should().Be(fixture.Trip.DepartureDateTime.AddMinutes(240));
        fixture.Outbox.Items.Should().ContainSingle(item => item.EventType == "trip.trip.route_changed");
    }

    [Fact]
    public async Task RouteOnlyChange_WithoutRouteDuration_UsesMaximumPositiveStopOffset()
    {
        var fixture = new Fixture();
        SetRouteDuration(fixture.NewRoute, null);
        var firstStopId = Guid.NewGuid();
        var lastStopId = Guid.NewGuid();
        fixture.RouteStops.Items.AddRange([
            RouteStop.Create(fixture.NewRoute.Id, firstStopId, 1, 35, 10m),
            RouteStop.Create(fixture.NewRoute.Id, lastStopId, 2, 95, 25m),
        ]);

        await fixture.Handler.Handle(fixture.Command(routeId: fixture.NewRoute.Id), default);

        fixture.Trip.EstimatedArrivalTime.Should().Be(fixture.Trip.DepartureDateTime.AddMinutes(95));
        fixture.Stops.Items.Single(stop => stop.StopId == firstStopId).EstimatedArrivalTime
            .Should().Be(fixture.Trip.DepartureDateTime.AddMinutes(35));
        fixture.Stops.Items.Single(stop => stop.StopId == lastStopId).EstimatedArrivalTime
            .Should().Be(fixture.Trip.DepartureDateTime.AddMinutes(95));
    }

    [Fact]
    public async Task RouteOnlyChange_WithoutAnyPositiveDuration_ReturnsFieldValidationAndRollsBack()
    {
        var fixture = new Fixture();
        SetRouteDuration(fixture.NewRoute, null);

        var action = () => fixture.Handler.Handle(fixture.Command(routeId: fixture.NewRoute.Id), default);

        var exception = await action.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainSingle(error =>
            error.Field == "estimatedArrivalTime"
            && error.Message == "Route duration or route-stop duration is required.");
        fixture.UnitOfWork.RollbackCount.Should().Be(1);
        fixture.UnitOfWork.SaveCount.Should().Be(0);
        fixture.UnitOfWork.CommitCount.Should().Be(0);
        fixture.Audits.Items.Should().BeEmpty();
        fixture.Outbox.Items.Should().BeEmpty();
        fixture.Swap.StageCount.Should().Be(0);
    }

    private sealed class Fixture
    {
        public Guid OperatorId { get; } = Guid.NewGuid();
        public Guid ActorId { get; } = Guid.NewGuid();
        public TripEntity Trip { get; }
        public Route NewRoute { get; }
        public FakeTripRepository Trips { get; }
        public FakeTripSeatRepository Seats { get; } = new();
        public FakeTripStopRepository Stops { get; } = new();
        public FakeTripStopFareRepository Fares { get; } = new();
        public FakeRouteStopRepository RouteStops { get; } = new();
        public FakeVehicleRepository Vehicles { get; } = new();
        public FakeBookingImpactClient Booking { get; }
        public FakeAuditRepository Audits { get; } = new();
        public FakeOutbox Outbox { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; }
        public FakeSwapService Swap { get; }
        public List<string> Calls { get; } = [];
        public EditTripCommandHandler Handler { get; }

        public Fixture(TimeSpan? departureOffset = null)
        {
            var oldRoute = CreateRoute(OperatorId, "Old route");
            NewRoute = CreateRoute(OperatorId, "New route");
            var oldVehicle = CreateVehicle(OperatorId, "51B-111.11", [new SeatLayoutItem("A01", "STANDARD", false)]);
            var departure = Now.Add(departureOffset ?? TimeSpan.FromHours(10));
            Trip = TripEntity.Create(
                OperatorId,
                oldRoute.Id,
                oldVehicle.Id,
                Guid.NewGuid(),
                null,
                null,
                departure,
                departure.AddHours(4),
                TripSource.MANUAL,
                Money.FromRaw(300_000),
                null,
                0m);
            Trips = new FakeTripRepository(Trip, Calls);
            Vehicles.Items.Add(oldVehicle);
            var routes = new FakeRouteRepository([oldRoute, NewRoute]);
            Booking = new FakeBookingImpactClient(Trip.Id, Calls);
            UnitOfWork = new FakeUnitOfWork(Calls);
            Swap = new FakeSwapService(Calls);
            Handler = new EditTripCommandHandler(
                Trips,
                Seats,
                Stops,
                Fares,
                routes,
                RouteStops,
                Vehicles,
                Booking,
                Swap,
                Audits,
                Outbox,
                UnitOfWork,
                new FrozenClock(Now),
                new DetailSender(() => CreateDetail(Trip)));
        }

        public EditTripCommand Command(long? baseFare = null, string? notes = null, Guid? vehicleId = null, Guid? routeId = null) =>
            new(
                Trip.Id,
                OperatorId,
                ActorId,
                "trace-22-8",
                baseFare.HasValue,
                baseFare,
                notes is not null,
                notes,
                vehicleId.HasValue,
                vehicleId,
                routeId.HasValue,
                routeId);
    }

    private sealed class FakeTripRepository(TripEntity trip, List<string> calls) : ITripRepository
    {
        public Action<TripEntity>? OnAcquire { get; set; }
        public Queue<bool> VehicleConflicts { get; } = new();
        public int VehicleConflictChecks { get; private set; }
        public Task<TripEntity?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<TripEntity?>(id == trip.Id ? trip : null);
        public Task<TripEntity?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken) => GetByIdAsync(tripId, cancellationToken);
        public Task<TripEntity?> AcquireForVehicleSwapAsync(Guid tripId, CancellationToken cancellationToken)
        {
            calls.Add("trip-lock");
            OnAcquire?.Invoke(trip);
            return GetByIdAsync(tripId, cancellationToken);
        }
        public Task<bool> HasVehicleConflictAsync(Guid vehicleId, DateTimeOffset departureDateTime, Guid excludedTripId, CancellationToken cancellationToken)
        {
            VehicleConflictChecks++;
            calls.Add(VehicleConflictChecks == 1
                ? "vehicle-conflict-preflight"
                : "vehicle-conflict-serialized");
            return Task.FromResult(VehicleConflicts.TryDequeue(out var conflict) && conflict);
        }
        public Task<TripEntity> AddAsync(TripEntity entity, CancellationToken ct) => Task.FromResult(entity);
        public void Update(TripEntity entity) { }
        public void Remove(TripEntity entity) { }
        public IQueryable<TripEntity> Query() => new[] { trip }.AsQueryable();
        public IQueryable<TripEntity> QueryNoTracking() => Query();
    }

    private sealed class FakeTripSeatRepository : ITripSeatRepository
    {
        public List<TripSeat> Items { get; } = [];
        public Action<List<TripSeat>>? OnAcquire { get; set; }
        public Task<IReadOnlyList<TripSeat>> AcquireForVehicleSwapAsync(Guid tripId, CancellationToken cancellationToken)
        {
            OnAcquire?.Invoke(Items);
            return Task.FromResult<IReadOnlyList<TripSeat>>(Items.Where(x => x.TripId == tripId).ToArray());
        }
        public Task<TripSeat?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(x => x.Id == id));
        public Task<TripSeat> AddAsync(TripSeat entity, CancellationToken ct) { Items.Add(entity); return Task.FromResult(entity); }
        public void Update(TripSeat entity) { }
        public void Remove(TripSeat entity) => Items.Remove(entity);
        public IQueryable<TripSeat> Query() => Items.AsQueryable();
        public IQueryable<TripSeat> QueryNoTracking() => Query();
    }

    private sealed class FakeTripStopRepository : ITripStopRepository
    {
        public List<TripStop> Items { get; } = [];
        public Task<IReadOnlyList<TripStop>> AcquireByTripAsync(Guid tripId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TripStop>>(Items.Where(x => x.TripId == tripId).ToArray());
        public Task DeleteByTripAsync(Guid tripId, CancellationToken cancellationToken) { Items.RemoveAll(x => x.TripId == tripId); return Task.CompletedTask; }
        public void RemoveRange(IEnumerable<TripStop> stops) { foreach (var stop in stops.ToArray()) Items.Remove(stop); }
        public Task<TripStop?> GetByIdAsync((Guid TripId, Guid StopId) id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(x => x.TripId == id.TripId && x.StopId == id.StopId));
        public Task<TripStop> AddAsync(TripStop entity, CancellationToken ct) { Items.Add(entity); return Task.FromResult(entity); }
        public void Update(TripStop entity) { }
        public void Remove(TripStop entity) => Items.Remove(entity);
        public IQueryable<TripStop> Query() => Items.AsQueryable();
        public IQueryable<TripStop> QueryNoTracking() => Query();
    }

    private sealed class FakeTripStopFareRepository : ITripStopFareRepository
    {
        public List<TripStopFare> Items { get; } = [];
        public Task<IReadOnlyList<TripStopFare>> AcquireByTripAsync(Guid tripId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TripStopFare>>(Items.Where(x => x.TripId == tripId).ToArray());
        public Task DeleteByTripAsync(Guid tripId, CancellationToken cancellationToken) { Items.RemoveAll(x => x.TripId == tripId); return Task.CompletedTask; }
        public void RemoveRange(IEnumerable<TripStopFare> fares) { foreach (var fare in fares.ToArray()) Items.Remove(fare); }
        public Task<TripStopFare?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(x => x.Id == id));
        public Task<TripStopFare> AddAsync(TripStopFare entity, CancellationToken ct) { Items.Add(entity); return Task.FromResult(entity); }
        public void Update(TripStopFare entity) { }
        public void Remove(TripStopFare entity) => Items.Remove(entity);
        public IQueryable<TripStopFare> Query() => Items.AsQueryable();
        public IQueryable<TripStopFare> QueryNoTracking() => Query();
    }

    private sealed class FakeRouteRepository(IEnumerable<Route> routes) : IRouteRepository
    {
        private readonly List<Route> items = routes.ToList();
        public Task<Route?> GetOwnedByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken) => Task.FromResult(items.SingleOrDefault(x => x.Id == routeId && x.OperatorId == operatorId));
        public Task<Route?> GetOwnedActiveByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken) => Task.FromResult(items.SingleOrDefault(x => x.Id == routeId && x.OperatorId == operatorId && x.IsActive && x.DeletedAt is null));
        public Task<Route?> AcquireOwnedActiveAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken) => GetOwnedActiveByIdAsync(operatorId, routeId, cancellationToken);
        public Task<IReadOnlyList<Route>> ListByOperatorAsync(Guid operatorId, string? search, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Route>>(items.Where(x => x.OperatorId == operatorId).ToArray());
        public Task<bool> ExistsActiveOwnedByOperatorAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken) => Task.FromResult(items.Any(x => x.Id == routeId && x.OperatorId == operatorId && x.IsActive));
        public Task<Route?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(items.SingleOrDefault(x => x.Id == id));
        public Task<Route> AddAsync(Route entity, CancellationToken ct) { items.Add(entity); return Task.FromResult(entity); }
        public void Update(Route entity) { }
        public void Remove(Route entity) => items.Remove(entity);
        public IQueryable<Route> Query() => items.AsQueryable();
        public IQueryable<Route> QueryNoTracking() => Query();
    }

    private sealed class FakeRouteStopRepository : IRouteStopRepository
    {
        public List<RouteStop> Items { get; } = [];
        public Task<IReadOnlyList<RouteStop>> AcquireByRouteAsync(Guid routeId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RouteStop>>(Items.Where(x => x.RouteId == routeId).OrderBy(x => x.OrderIndex).ToArray());
        public Task<bool> ExistsByRouteAndOrderIndexAsync(Guid routeId, int orderIndex, CancellationToken cancellationToken) => Task.FromResult(Items.Any(x => x.RouteId == routeId && x.OrderIndex == orderIndex));
        public Task<RouteStop?> GetByRouteAndStopAsync(Guid routeId, Guid stopId, CancellationToken cancellationToken) => Task.FromResult(Items.SingleOrDefault(x => x.RouteId == routeId && x.StopId == stopId));
        public Task<RouteStop?> GetByIdAsync((Guid RouteId, Guid StopId) id, CancellationToken ct) => GetByRouteAndStopAsync(id.RouteId, id.StopId, ct);
        public Task<RouteStop> AddAsync(RouteStop entity, CancellationToken ct) { Items.Add(entity); return Task.FromResult(entity); }
        public void Update(RouteStop entity) { }
        public void Remove(RouteStop entity) => Items.Remove(entity);
        public IQueryable<RouteStop> Query() => Items.AsQueryable();
        public IQueryable<RouteStop> QueryNoTracking() => Query();
    }

    private sealed class FakeVehicleRepository : IVehicleRepository
    {
        public List<Vehicle> Items { get; } = [];
        public Action<IReadOnlyList<Vehicle>>? OnAcquire { get; set; }
        public Task<Vehicle?> GetOwnedByIdAsync(Guid operatorId, Guid vehicleId, CancellationToken cancellationToken) => Task.FromResult(Items.SingleOrDefault(x => x.OperatorId == operatorId && x.Id == vehicleId && x.DeletedAt is null));
        public Task<IReadOnlyList<Vehicle>> AcquireForVehicleSwapAsync(Guid operatorId, IReadOnlyCollection<Guid> vehicleIds, CancellationToken cancellationToken)
        {
            IReadOnlyList<Vehicle> acquired = Items
                .Where(x => x.OperatorId == operatorId && vehicleIds.Contains(x.Id) && x.DeletedAt is null)
                .OrderBy(x => x.Id)
                .ToArray();
            OnAcquire?.Invoke(acquired);
            return Task.FromResult(acquired);
        }
        public Task<PagedResult<Vehicle>> ListByOperatorAsync(Guid operatorId, int page, int pageSize, string? search, string? searchIn, string? sortBy, string sortDir, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> LicensePlateExistsAsync(string licensePlate, Guid? excludedVehicleId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> TryAddAsync(Vehicle vehicle, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> TryUpdateAsync(Vehicle vehicle, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<Vehicle?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(x => x.Id == id));
        public Task<Vehicle> AddAsync(Vehicle entity, CancellationToken ct) { Items.Add(entity); return Task.FromResult(entity); }
        public void Update(Vehicle entity) { }
        public void Remove(Vehicle entity) => Items.Remove(entity);
        public IQueryable<Vehicle> Query() => Items.AsQueryable();
        public IQueryable<Vehicle> QueryNoTracking() => Query();
    }

    private sealed class FakeBookingImpactClient(Guid tripId, List<string> calls) : IBookingImpactClient
    {
        public TripBookingImpactProjection Projection { get; set; } = new(tripId, 0, []);
        public int CallCount { get; private set; }
        public Task<int> GetActiveBookingCountByStopAsync(Guid stopId, Guid operatorId, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<TripBookingImpactProjection> GetTripEditImpactAsync(Guid requestedTripId, Guid operatorId, CancellationToken cancellationToken)
        {
            CallCount++;
            calls.Add("booking-preflight");
            return Task.FromResult(Projection);
        }
    }

    private sealed class FakeSwapService(List<string> calls) : ITripVehicleSwapService
    {
        public int StageCount { get; private set; }
        public IReadOnlyCollection<VehicleSwapBookingSeatImpact> LastImpacts { get; private set; } = [];
        public Task<bool> StageSwapAsync(TripEntity trip, Vehicle oldVehicle, Vehicle newVehicle, IReadOnlyCollection<TripSeat> lockedSeats, IReadOnlyCollection<VehicleSwapBookingSeatImpact> bookingSeatImpacts, Guid actorUserId, string auditAction, string requestId, DateTimeOffset occurredAt, CancellationToken cancellationToken)
        {
            StageCount++;
            LastImpacts = bookingSeatImpacts;
            calls.Add("swap");
            return Task.FromResult(true);
        }
    }

    private sealed class FakeAuditRepository : ITripAuditLogRepository
    {
        public List<TripAuditLog> Items { get; } = [];
        public Task AddAsync(TripAuditLog auditLog, CancellationToken cancellationToken = default) { Items.Add(auditLog); return Task.CompletedTask; }
        public Task<IReadOnlyList<TripAuditLog>> ListByTripIdAsync(Guid tripId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TripAuditLog>>(Items.Where(x => x.TripId == tripId).ToArray());
    }

    private sealed class FakeOutbox : IIntegrationEventOutbox
    {
        public List<(string EventType, string Payload)> Items { get; } = [];
        public Task EnqueueAsync(string eventType, string payloadJson, CancellationToken ct = default) { Items.Add((eventType, payloadJson)); return Task.CompletedTask; }
    }

    private sealed class FakeUnitOfWork(List<string> calls) : IUnitOfWork
    {
        public int BeginCount { get; private set; }
        public int SaveCount { get; private set; }
        public int CommitCount { get; private set; }
        public int RollbackCount { get; private set; }
        public bool ThrowOnSave { get; set; }
        public Action? OnRollback { get; set; }
        public Task BeginTransactionAsync(CancellationToken ct) { BeginCount++; calls.Add("begin"); return Task.CompletedTask; }
        public Task<int> SaveChangesAsync(CancellationToken ct)
        {
            SaveCount++;
            calls.Add("save");
            return ThrowOnSave
                ? Task.FromException<int>(new InvalidOperationException("Simulated save failure."))
                : Task.FromResult(1);
        }
        public Task CommitAsync(CancellationToken ct) { CommitCount++; calls.Add("commit"); return Task.CompletedTask; }
        public Task RollbackAsync(CancellationToken ct) { RollbackCount++; calls.Add("rollback"); OnRollback?.Invoke(); return Task.CompletedTask; }
        public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct) => await operation();
    }

    private sealed class FrozenClock(DateTimeOffset value) : IClock { public DateTimeOffset UtcNow => value; }

    private sealed class DetailSender(Func<TripDetailDto> detail) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => Task.FromResult((TResponse)(object)detail());
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => Task.FromResult<object?>(detail());
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => Empty<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => Empty<object?>();
        private static async IAsyncEnumerable<T> Empty<T>() { await Task.CompletedTask; yield break; }
    }

    private static Route CreateRoute(Guid operatorId, string name) => Route.Create(operatorId, name, Guid.NewGuid(), Guid.NewGuid(), Money.FromRaw(300_000), 100m, 240);

    private static void SetRouteDuration(Route route, int? estimatedDurationMinutes) =>
        route.UpdateDetails(
            route.Name,
            route.OriginStationId,
            route.DestinationStationId,
            route.BaseFare,
            route.TotalDistanceKm,
            estimatedDurationMinutes,
            route.ReturnRouteId);

    private static Vehicle CreateVehicle(Guid operatorId, string plate, IReadOnlyCollection<SeatLayoutItem> seats)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { seats }));
        return Vehicle.Create(operatorId, Guid.NewGuid(), plate, document.RootElement, Math.Max(1, seats.Count), null, null);
    }

    private static TripDetailDto CreateDetail(TripEntity trip) => new(
        trip.Id,
        trip.OperatorId,
        trip.RouteId,
        trip.VehicleId,
        trip.Status.ToString(),
        trip.DepartureDateTime,
        trip.EstimatedArrivalTime,
        trip.BaseFare.Amount,
        new TripStationDto(Guid.NewGuid(), "Origin"),
        new TripStationDto(Guid.NewGuid(), "Destination"),
        [],
        new TripSeatSummaryDto(0, 0),
        null,
        new TripFareBreakdownDto(trip.BaseFare.Amount, []))
    { Notes = trip.Notes };

    private sealed record SeatLayoutItem(string SeatNumber, string Type, bool Disabled);
}
