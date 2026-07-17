using System.Text.Json;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Jobs;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Events;
using VietRide.Trip.Application.Features.Stops;
using VietRide.Trip.Application.Features.Vehicles;
using VietRide.Trip.Domain.Constants;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.DriverSchedules;

public sealed class UpdateDriverScheduleHandler
    : IRequestHandler<UpdateDriverScheduleCommand, DriverScheduleDto>
{
    private const string CrewChangedEventType = "trip.trip.crew_changed";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan IctOffset = TimeSpan.FromHours(7);

    private readonly IDriverScheduleRepository schedules;
    private readonly IDriverScheduleAuditLogRepository scheduleAudits;
    private readonly ITripRepository trips;
    private readonly ITripSeatRepository tripSeats;
    private readonly ITripStopRepository tripStops;
    private readonly ITripAuditLogRepository tripAudits;
    private readonly IVehicleRepository vehicles;
    private readonly IRouteRepository routes;
    private readonly IIdentityInternalClient identity;
    private readonly IBookingImpactClient bookingImpact;
    private readonly ITripVehicleSwapService vehicleSwap;
    private readonly IIntegrationEventOutbox outbox;
    private readonly ITripGenerationJobScheduler generationJobs;
    private readonly IUnitOfWork unitOfWork;
    private readonly IClock clock;

    public UpdateDriverScheduleHandler(
        IDriverScheduleRepository schedules,
        IDriverScheduleAuditLogRepository scheduleAudits,
        ITripRepository trips,
        ITripSeatRepository tripSeats,
        ITripStopRepository tripStops,
        ITripAuditLogRepository tripAudits,
        IVehicleRepository vehicles,
        IRouteRepository routes,
        IIdentityInternalClient identity,
        IBookingImpactClient bookingImpact,
        ITripVehicleSwapService vehicleSwap,
        IIntegrationEventOutbox outbox,
        ITripGenerationJobScheduler generationJobs,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        this.schedules = schedules;
        this.scheduleAudits = scheduleAudits;
        this.trips = trips;
        this.tripSeats = tripSeats;
        this.tripStops = tripStops;
        this.tripAudits = tripAudits;
        this.vehicles = vehicles;
        this.routes = routes;
        this.identity = identity;
        this.bookingImpact = bookingImpact;
        this.vehicleSwap = vehicleSwap;
        this.outbox = outbox;
        this.generationJobs = generationJobs;
        this.unitOfWork = unitOfWork;
        this.clock = clock;
    }

    public async Task<DriverScheduleDto> Handle(
        UpdateDriverScheduleCommand request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var schedule = await schedules.GetByIdAsync(request.DriverScheduleId, cancellationToken);
        if (schedule is null || schedule.OperatorId != request.OperatorId)
        {
            throw ScheduleNotFound();
        }

        var before = ScheduleState.From(schedule);
        var effective = BuildEffectiveState(request, before);
        var changedFields = GetChangedFields(before, effective);
        if (changedFields.Count == 0)
        {
            return DriverScheduleMapper.ToDto(schedule);
        }

        ValidateLocalRules(request, schedule.ValidFrom, effective);
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(identity, request.OperatorId, cancellationToken);
        await ValidateReferencesAsync(request, effective, changedFields, cancellationToken);

        Vehicle? newVehicle = null;
        if (changedFields.Contains("vehicleId"))
        {
            newVehicle = effective.VehicleId.HasValue
                ? await vehicles.GetOwnedByIdAsync(request.OperatorId, effective.VehicleId.Value, cancellationToken)
                : null;
            if (effective.VehicleId.HasValue && newVehicle is null)
            {
                throw new CodedNotFoundException("VEHICLE_NOT_FOUND", "Vehicle was not found.");
            }

            if (newVehicle is not null && (!newVehicle.IsActive || newVehicle.DeletedAt is not null || newVehicle.Status != VehicleStatus.ACTIVE))
            {
                throw ValidationFailure("vehicleId", "Vehicle must be active.");
            }
        }

        await ValidateOverlapsAsync(schedule.Id, schedule.ValidFrom, effective, cancellationToken);

        if (request.ApplyTo == UpdateDriverScheduleCommand.FutureOnly)
        {
            return await ApplyFutureOnlyAsync(
                request,
                before,
                effective,
                changedFields,
                newVehicle,
                now,
                cancellationToken);
        }

        var pending = await trips.ListPendingByDriverScheduleAsync(schedule.Id, cancellationToken);
        var projections = new Dictionary<Guid, TripBookingImpactProjection>();
        foreach (var trip in pending)
        {
            projections[trip.Id] = await bookingImpact.GetTripEditImpactAsync(
                trip.Id,
                request.OperatorId,
                cancellationToken);
        }

        var preflightSeats = new Dictionary<Guid, IReadOnlyList<TripSeat>>();
        if (changedFields.Contains("vehicleId") && newVehicle is not null)
        {
            foreach (var trip in pending)
            {
                preflightSeats[trip.Id] = tripSeats.QueryNoTracking()
                    .Where(seat => seat.TripId == trip.Id)
                    .OrderBy(seat => seat.SeatNumber)
                    .ThenBy(seat => seat.Id)
                    .ToArray();
            }

            ApplyVehicleConflictsFirst(pending, preflightSeats, newVehicle, now);
        }

        var tripMutatingFields = changedFields.Any(field => field is
            "departureTime" or "dayOfWeek" or "driverUserId" or "assistantUserId" or "vehicleId");
        if (tripMutatingFields)
        {
            var tooLate = pending.Any(trip =>
                projections[trip.Id].ActiveBookings.Any(booking =>
                    string.Equals(booking.Status, "CONFIRMED", StringComparison.OrdinalIgnoreCase))
                && IsInsideConfirmedBookingCutoff(trip, effective, changedFields, now));
            if (tooLate)
            {
                throw new CodedConflictException(
                    "DRIVER_SCHEDULE_EDIT_TOO_LATE",
                    "DriverSchedule changes require both old and new departures to remain at least two hours away for a confirmed Booking.");
            }
        }

        return await ApplyAllPendingAsync(
            request,
            before,
            effective,
            changedFields,
            pending,
            projections,
            preflightSeats,
            newVehicle,
            now,
            cancellationToken);
    }

    private async Task<DriverScheduleDto> ApplyFutureOnlyAsync(
        UpdateDriverScheduleCommand request,
        ScheduleState before,
        ScheduleState effective,
        IReadOnlyList<string> changedFields,
        Vehicle? newVehicle,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        DriverSchedule locked;
        try
        {
            locked = await schedules.AcquireOwnedForUpdateAsync(
                request.DriverScheduleId,
                request.OperatorId,
                cancellationToken) ?? throw ScheduleNotFound();
            EnsureUnchanged(before, locked);
            await AcquireOverlapLocksAsync(locked.ValidFrom, effective, cancellationToken);
            await ValidateOverlapsAsync(locked.Id, locked.ValidFrom, effective, cancellationToken);

            if (changedFields.Contains("vehicleId") && newVehicle is not null)
            {
                var newVehicleId = newVehicle.Id;
                var lockedVehicles = await vehicles.AcquireForVehicleSwapAsync(
                    request.OperatorId,
                    [newVehicleId],
                    cancellationToken);
                newVehicle = lockedVehicles.SingleOrDefault(vehicle => vehicle.Id == newVehicleId);
                if (newVehicle is null)
                {
                    throw new CodedNotFoundException("VEHICLE_NOT_FOUND", "Vehicle was not found.");
                }

                EnsureReplacementVehicleCanBeAssigned(newVehicle, request.OperatorId);
            }

            ApplySchedule(locked, effective, now);
            await AddScheduleAuditAsync(locked.Id, request, changedFields, before, effective, now, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        if (locked.IsActive)
        {
            generationJobs.EnqueueScheduleGeneration(locked.Id);
        }

        return DriverScheduleMapper.ToDto(locked);
    }

    private async Task<DriverScheduleDto> ApplyAllPendingAsync(
        UpdateDriverScheduleCommand request,
        ScheduleState before,
        ScheduleState effective,
        IReadOnlyList<string> changedFields,
        IReadOnlyList<Domain.Entities.Trip> preflightTrips,
        IReadOnlyDictionary<Guid, TripBookingImpactProjection> projections,
        IReadOnlyDictionary<Guid, IReadOnlyList<TripSeat>> preflightSeats,
        Vehicle? newVehicle,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        DriverSchedule lockedSchedule;
        try
        {
            lockedSchedule = await schedules.AcquireOwnedForUpdateAsync(
                request.DriverScheduleId,
                request.OperatorId,
                cancellationToken) ?? throw ScheduleNotFound();
            EnsureUnchanged(before, lockedSchedule);
            await AcquireOverlapLocksAsync(lockedSchedule.ValidFrom, effective, cancellationToken);
            await ValidateOverlapsAsync(lockedSchedule.Id, lockedSchedule.ValidFrom, effective, cancellationToken);

            var currentPendingTrips = await trips.ListPendingByDriverScheduleAsync(
                lockedSchedule.Id,
                cancellationToken);
            if (!BuildPendingTripSnapshot(preflightTrips)
                .SequenceEqual(BuildPendingTripSnapshot(currentPendingTrips)))
            {
                throw StaleConflict();
            }

            var lockedTrips = new List<LockedTrip>(preflightTrips.Count);
            foreach (var preflight in preflightTrips.OrderBy(trip => trip.DepartureDateTime).ThenBy(trip => trip.Id))
            {
                var trip = await trips.AcquireForVehicleSwapAsync(preflight.Id, cancellationToken)
                    ?? throw StaleConflict();
                if (trip.DriverScheduleId != lockedSchedule.Id
                    || trip.Status is not (TripStatus.SCHEDULED or TripStatus.BOARDING)
                    || trip.DepartureDateTime != preflight.DepartureDateTime
                    || trip.VehicleId != preflight.VehicleId
                    || trip.DriverUserId != preflight.DriverUserId
                    || trip.AssistantUserId != preflight.AssistantUserId)
                {
                    throw StaleConflict();
                }

                var seats = await tripSeats.AcquireForVehicleSwapAsync(trip.Id, cancellationToken);
                var stops = await tripStops.AcquireByTripAsync(trip.Id, cancellationToken);
                if (preflightSeats.TryGetValue(trip.Id, out var expectedSeats)
                    && !BuildSeatSnapshot(expectedSeats).SequenceEqual(BuildSeatSnapshot(seats)))
                {
                    throw StaleConflict();
                }

                lockedTrips.Add(new LockedTrip(trip, seats, stops));
            }

            IReadOnlyList<Vehicle> lockedVehicles = [];
            if (changedFields.Contains("vehicleId") && newVehicle is not null)
            {
                var vehicleIds = lockedTrips.Select(item => item.Trip.VehicleId)
                    .Append(newVehicle.Id)
                    .Distinct()
                    .Order()
                    .ToArray();
                lockedVehicles = await vehicles.AcquireForVehicleSwapAsync(
                    request.OperatorId,
                    vehicleIds,
                    cancellationToken);
                if (lockedVehicles.Count != vehicleIds.Length)
                {
                    throw new CodedNotFoundException("VEHICLE_NOT_FOUND", "Vehicle was not found.");
                }

                newVehicle = lockedVehicles.Single(vehicle => vehicle.Id == newVehicle.Id);
                EnsureReplacementVehicleCanBeAssigned(newVehicle, request.OperatorId);
            }

            if (changedFields.Contains("vehicleId") && newVehicle is not null)
            {
                ApplyVehicleConflictsFirst(
                    lockedTrips.Select(item => item.Trip).ToArray(),
                    lockedTrips.ToDictionary(item => item.Trip.Id, item => (IReadOnlyList<TripSeat>)item.Seats),
                    newVehicle,
                    now);
            }

            ApplySchedule(lockedSchedule, effective, now);
            await AddScheduleAuditAsync(
                lockedSchedule.Id,
                request,
                changedFields,
                before,
                effective,
                now,
                cancellationToken);

            foreach (var item in lockedTrips)
            {
                await ApplyTripCascadeAsync(
                    request,
                    effective,
                    changedFields,
                    item,
                    projections[item.Trip.Id],
                    newVehicle,
                    lockedVehicles,
                    now,
                    cancellationToken);
            }

            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        if (lockedSchedule.IsActive)
        {
            generationJobs.EnqueueScheduleGeneration(lockedSchedule.Id);
        }

        return DriverScheduleMapper.ToDto(lockedSchedule);
    }

    private async Task ApplyTripCascadeAsync(
        UpdateDriverScheduleCommand request,
        ScheduleState effective,
        IReadOnlyCollection<string> changedFields,
        LockedTrip item,
        TripBookingImpactProjection projection,
        Vehicle? newVehicle,
        IReadOnlyList<Vehicle> lockedVehicles,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var trip = item.Trip;
        var localDate = DateOnly.FromDateTime(trip.DepartureDateTime.ToOffset(IctOffset).DateTime);
        var contractDay = ToContractDayOfWeek(localDate);
        if (changedFields.Contains("dayOfWeek") && !effective.DayOfWeek.Contains(contractDay))
        {
            var previousStatus = trip.Status;
            trip.Cancel(now, request.ActorUserId, TripCancelledIntegrationEvent.DriverScheduleDayRemovedReason);
            var cancelled = new TripCancelledIntegrationEvent(
                Guid.NewGuid(),
                now,
                trip.Id,
                trip.OperatorId,
                now,
                TripCancelledIntegrationEvent.DriverScheduleDayRemovedReason);
            await outbox.EnqueueAsync(cancelled.EventType, JsonSerializer.Serialize(cancelled, JsonOptions), cancellationToken);
            await AddTripAuditAsync(
                trip.Id,
                request,
                ["status"],
                new { status = previousStatus.ToString() },
                new { status = TripStatus.CANCELLED.ToString(), cancelReason = cancelled.CancelReason },
                now,
                cancellationToken);
            return;
        }

        var cascadeFields = new List<string>();
        var cascadeBefore = new Dictionary<string, object?>();
        var cascadeAfter = new Dictionary<string, object?>();

        if (changedFields.Contains("departureTime"))
        {
            var oldDeparture = trip.DepartureDateTime;
            var newDeparture = BuildDepartureDateTime(localDate, effective.DepartureTime);
            if (newDeparture != oldDeparture)
            {
                var delta = newDeparture - oldDeparture;
                trip.Reschedule(newDeparture, trip.EstimatedArrivalTime.Add(delta));
                foreach (var stop in item.Stops)
                {
                    stop.RecomputePlannedArrival(stop.EstimatedArrivalTime.Add(delta));
                }

                var scheduleChanged = new TripScheduleChangedIntegrationEvent(
                    Guid.NewGuid(),
                    now,
                    trip.Id,
                    trip.OperatorId,
                    oldDeparture,
                    newDeparture,
                    TripScheduleChangedIntegrationEvent.ClassifySeverity(oldDeparture, newDeparture));
                await outbox.EnqueueAsync(
                    scheduleChanged.EventId,
                    scheduleChanged.EventType,
                    JsonSerializer.Serialize(scheduleChanged, JsonOptions),
                    cancellationToken);
                cascadeFields.Add("departureDateTime");
                cascadeBefore["departureDateTime"] = oldDeparture;
                cascadeAfter["departureDateTime"] = newDeparture;
            }
        }

        if (changedFields.Contains("driverUserId") || changedFields.Contains("assistantUserId"))
        {
            var oldDriver = trip.DriverUserId;
            var oldAssistant = trip.AssistantUserId;
            trip.ChangeCrew(effective.DriverUserId, effective.AssistantUserId);
            var routeName = routes.QueryNoTracking()
                .Where(route => route.Id == trip.RouteId)
                .Select(route => route.Name)
                .FirstOrDefault() ?? "Trip";
            var vehiclePlate = vehicles.QueryNoTracking()
                .Where(vehicle => vehicle.Id == trip.VehicleId)
                .Select(vehicle => vehicle.LicensePlate)
                .FirstOrDefault();
            await outbox.EnqueueAsync(
                CrewChangedEventType,
                JsonSerializer.Serialize(new
                {
                    tripId = trip.Id,
                    operatorId = trip.OperatorId,
                    oldDriverUserId = oldDriver,
                    oldAssistantUserId = oldAssistant,
                    driverUserId = trip.DriverUserId,
                    assistantUserId = trip.AssistantUserId,
                    routeName,
                    vehiclePlateNumber = vehiclePlate,
                    departureDateTime = trip.DepartureDateTime,
                }, JsonOptions),
                cancellationToken);
            cascadeFields.AddRange(changedFields.Where(field => field is "driverUserId" or "assistantUserId"));
            cascadeBefore["driverUserId"] = oldDriver;
            cascadeBefore["assistantUserId"] = oldAssistant;
            cascadeAfter["driverUserId"] = trip.DriverUserId;
            cascadeAfter["assistantUserId"] = trip.AssistantUserId;
        }

        if (changedFields.Contains("vehicleId") && newVehicle is not null && trip.VehicleId != newVehicle.Id)
        {
            if (await trips.HasVehicleConflictAsync(
                    newVehicle.Id,
                    trip.DepartureDateTime,
                    trip.Id,
                    cancellationToken))
            {
                throw new CodedConflictException("TRIP_VEHICLE_CONFLICT", "Vehicle is assigned to another Trip at this departure time.");
            }

            var oldVehicle = lockedVehicles.Single(vehicle => vehicle.Id == trip.VehicleId);
            var classifications = ClassifySeats(item.Seats, newVehicle);
            var impacts = BuildBookingSeatImpacts(projection, item.Seats, classifications);
            await vehicleSwap.StageSwapAsync(
                trip,
                oldVehicle,
                newVehicle,
                item.Seats,
                impacts,
                request.ActorUserId,
                TripAuditAction.DriverScheduleCascadeApplied,
                request.RequestId,
                now,
                cancellationToken);
        }

        if (cascadeFields.Count > 0)
        {
            await AddTripAuditAsync(
                trip.Id,
                request,
                cascadeFields.Distinct(StringComparer.Ordinal).ToArray(),
                cascadeBefore,
                cascadeAfter,
                now,
                cancellationToken);
        }
    }

    private static ScheduleState BuildEffectiveState(UpdateDriverScheduleCommand request, ScheduleState before) =>
        new(
            request.DepartureTimeSpecified ? request.DepartureTime!.Value : before.DepartureTime,
            request.DayOfWeekSpecified ? NormalizeDays(request.DayOfWeek!) : before.DayOfWeek,
            request.DriverUserIdSpecified ? request.DriverUserId!.Value : before.DriverUserId,
            request.AssistantUserIdSpecified ? request.AssistantUserId : before.AssistantUserId,
            request.VehicleIdSpecified ? request.VehicleId : before.VehicleId,
            request.ValidUntilSpecified ? request.ValidUntil : before.ValidUntil,
            request.IsActiveSpecified ? request.IsActive!.Value : before.IsActive);

    private static IReadOnlyList<string> GetChangedFields(ScheduleState before, ScheduleState after)
    {
        var changed = new List<string>();
        if (before.DepartureTime != after.DepartureTime) changed.Add("departureTime");
        if (!before.DayOfWeek.SequenceEqual(after.DayOfWeek)) changed.Add("dayOfWeek");
        if (before.DriverUserId != after.DriverUserId) changed.Add("driverUserId");
        if (before.AssistantUserId != after.AssistantUserId) changed.Add("assistantUserId");
        if (before.VehicleId != after.VehicleId) changed.Add("vehicleId");
        if (before.ValidUntil != after.ValidUntil) changed.Add("validUntil");
        if (before.IsActive != after.IsActive) changed.Add("isActive");
        return changed;
    }

    private static void ValidateLocalRules(
        UpdateDriverScheduleCommand request,
        DateOnly validFrom,
        ScheduleState effective)
    {
        if (effective.ValidUntil < validFrom)
        {
            throw ValidationFailure("validUntil", "validUntil cannot precede validFrom.");
        }

        if (request.ApplyTo == UpdateDriverScheduleCommand.AllPending && effective.VehicleId is null)
        {
            throw ValidationFailure("vehicleId", "ALL_PENDING requires a concrete vehicleId.");
        }
    }

    private async Task ValidateReferencesAsync(
        UpdateDriverScheduleCommand request,
        ScheduleState effective,
        IReadOnlyCollection<string> changedFields,
        CancellationToken cancellationToken)
    {
        if (changedFields.Contains("driverUserId"))
        {
            await ValidateIdentityUserAsync(
                request.OperatorId,
                effective.DriverUserId,
                "DRIVER",
                "driverUserId",
                cancellationToken);
        }

        if (changedFields.Contains("assistantUserId") && effective.AssistantUserId.HasValue)
        {
            await ValidateIdentityUserAsync(
                request.OperatorId,
                effective.AssistantUserId.Value,
                "ASSISTANT",
                "assistantUserId",
                cancellationToken);
        }

    }

    private Task AcquireOverlapLocksAsync(
        DateOnly validFrom,
        ScheduleState effective,
        CancellationToken cancellationToken) =>
        schedules.AcquireOverlapLocksAsync(
            effective.DriverUserId,
            effective.AssistantUserId,
            effective.VehicleId,
            effective.DayOfWeek,
            effective.DepartureTime,
            validFrom,
            effective.ValidUntil,
            cancellationToken);

    private async Task ValidateIdentityUserAsync(
        Guid operatorId,
        Guid userId,
        string role,
        string field,
        CancellationToken cancellationToken)
    {
        var user = await identity.GetUserAsync(userId, cancellationToken);
        if (!user.Found
            || user.OperatorId != operatorId
            || !string.Equals(user.Role, role, StringComparison.OrdinalIgnoreCase))
        {
            throw ValidationFailure(field, $"Identity user must have role {role} under the caller operator.");
        }
    }

    private async Task ValidateOverlapsAsync(
        Guid scheduleId,
        DateOnly validFrom,
        ScheduleState effective,
        CancellationToken cancellationToken)
    {
        if (!effective.IsActive)
        {
            return;
        }

        if (await schedules.HasDriverConflictAsync(
                effective.DriverUserId,
                effective.DayOfWeek,
                effective.DepartureTime,
                validFrom,
                effective.ValidUntil,
                scheduleId,
                cancellationToken))
        {
            throw new CodedConflictException("TRIP_DRIVER_CONFLICT", "Driver has a conflicting active schedule.");
        }

        if (effective.AssistantUserId.HasValue
            && await schedules.HasAssistantConflictAsync(
                effective.AssistantUserId.Value,
                effective.DayOfWeek,
                effective.DepartureTime,
                validFrom,
                effective.ValidUntil,
                scheduleId,
                cancellationToken))
        {
            throw new CodedConflictException("TRIP_DRIVER_CONFLICT", "Assistant has a conflicting active schedule.");
        }

        if (effective.VehicleId.HasValue
            && await schedules.HasVehicleConflictAsync(
                effective.VehicleId.Value,
                effective.DayOfWeek,
                effective.DepartureTime,
                validFrom,
                effective.ValidUntil,
                scheduleId,
                cancellationToken))
        {
            throw new CodedConflictException("TRIP_VEHICLE_CONFLICT", "Vehicle has a conflicting active schedule.");
        }
    }

    private static void ApplySchedule(DriverSchedule schedule, ScheduleState state, DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(state.DayOfWeek));
        schedule.UpdateRecurrence(
            state.DepartureTime,
            document.RootElement,
            state.DriverUserId,
            state.AssistantUserId,
            state.VehicleId,
            state.ValidUntil,
            state.IsActive);
        schedule.UpdatedAt = now;
    }

    private async Task AddScheduleAuditAsync(
        Guid scheduleId,
        UpdateDriverScheduleCommand request,
        IReadOnlyCollection<string> changedFields,
        ScheduleState before,
        ScheduleState after,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await scheduleAudits.AddAsync(
            DriverScheduleAuditLog.Create(
                Guid.NewGuid(),
                scheduleId,
                request.ActorUserId,
                DriverScheduleAuditAction.DriverScheduleEdited,
                JsonSerializer.Serialize(new { changedFields, before, after, requestId = request.RequestId }, JsonOptions),
                now),
            cancellationToken);

    private async Task AddTripAuditAsync(
        Guid tripId,
        UpdateDriverScheduleCommand request,
        IReadOnlyCollection<string> changedFields,
        object before,
        object after,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await tripAudits.AddAsync(
            TripAuditLog.Create(
                Guid.NewGuid(),
                tripId,
                request.ActorUserId,
                TripAuditAction.DriverScheduleCascadeApplied,
                JsonSerializer.Serialize(new { changedFields, before, after, requestId = request.RequestId }, JsonOptions),
                now),
            cancellationToken);

    private static void ApplyVehicleConflictsFirst(
        IReadOnlyCollection<Domain.Entities.Trip> pending,
        IReadOnlyDictionary<Guid, IReadOnlyList<TripSeat>> seatsByTrip,
        Vehicle newVehicle,
        DateTimeOffset now)
    {
        var heldConflict = false;
        var tooLate = false;
        foreach (var trip in pending)
        {
            var seats = seatsByTrip[trip.Id];
            var classifications = ClassifySeats(seats, newVehicle);
            var incompatibleHeld = seats.Any(seat =>
                seat.Status == TripSeatStatus.HELD
                && classifications.GetValueOrDefault(seat.SeatNumber) is not null);
            var incompatibleBooked = seats.Any(seat =>
                seat.Status == TripSeatStatus.BOOKED
                && classifications.GetValueOrDefault(seat.SeatNumber) is not null);
            if (trip.Status == TripStatus.SCHEDULED && incompatibleHeld)
            {
                heldConflict = true;
            }

            var deadline = Min(now.AddHours(4), trip.DepartureDateTime.AddMinutes(-30));
            if (trip.Status == TripStatus.BOARDING && (incompatibleHeld || incompatibleBooked)
                || incompatibleBooked && deadline <= now)
            {
                tooLate = true;
            }
        }

        if (heldConflict)
        {
            throw new CodedConflictException(
                "TRIP_VEHICLE_SWAP_HELD_SEAT_CONFLICT",
                "Vehicle swap would make a held passenger seat incompatible.");
        }

        if (tooLate)
        {
            throw new CodedConflictException(
                "TRIP_VEHICLE_SWAP_TOO_LATE",
                "Vehicle swap is too late for an incompatible passenger seat.");
        }
    }

    private static IReadOnlyDictionary<string, string?> ClassifySeats(
        IReadOnlyCollection<TripSeat> seats,
        Vehicle newVehicle)
    {
        var layout = newVehicle.SeatLayoutJson.Deserialize<SeatLayoutDto>(JsonOptions)
            ?? throw new CodedValidationException("VALIDATION_ERROR", "Vehicle seat layout is invalid.");
        var layoutSeats = layout.Seats.ToDictionary(
            seat => NormalizeSeatNumber(seat.SeatNumber),
            seat => seat,
            StringComparer.Ordinal);
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var oldSeat in seats.Where(seat => seat.Status is TripSeatStatus.HELD or TripSeatStatus.BOOKED))
        {
            if (!layoutSeats.TryGetValue(oldSeat.SeatNumber, out var newSeat))
            {
                result[oldSeat.SeatNumber] = VehicleSwapBookingSeatImpact.SeatRemoved;
            }
            else if (!Enum.TryParse<TripSeatType>(newSeat.Type, true, out var newType) || !Enum.IsDefined(newType))
            {
                throw new CodedValidationException("VALIDATION_ERROR", "Vehicle seat layout contains an unknown seat type.");
            }
            else if (newSeat.Disabled || newType == TripSeatType.DRIVER_AREA)
            {
                result[oldSeat.SeatNumber] = VehicleSwapBookingSeatImpact.SeatDisabled;
            }
            else
            {
                result[oldSeat.SeatNumber] = PassengerRank(newType) < PassengerRank(oldSeat.SeatType)
                    ? VehicleSwapBookingSeatImpact.SeatTypeDowngraded
                    : null;
            }
        }

        return result;
    }

    private static IReadOnlyCollection<VehicleSwapBookingSeatImpact> BuildBookingSeatImpacts(
        TripBookingImpactProjection projection,
        IReadOnlyCollection<TripSeat> seats,
        IReadOnlyDictionary<string, string?> classifications)
    {
        var booked = seats.Where(seat => seat.Status == TripSeatStatus.BOOKED)
            .Select(seat => seat.SeatNumber)
            .ToHashSet(StringComparer.Ordinal);
        var impacts = new List<VehicleSwapBookingSeatImpact>();
        foreach (var booking in projection.ActiveBookings.OrderBy(booking => booking.BookingId))
        {
            var groups = booking.SeatNumbers
                .Select(NormalizeSeatNumber)
                .Where(booked.Contains)
                .Select(seatNumber => new { SeatNumber = seatNumber, Reason = classifications.GetValueOrDefault(seatNumber) })
                .Where(item => item.Reason is not null)
                .GroupBy(item => item.Reason!, StringComparer.Ordinal);
            foreach (var group in groups)
            {
                impacts.Add(new VehicleSwapBookingSeatImpact(
                    booking.BookingId,
                    group.Select(item => item.SeatNumber).ToArray(),
                    group.Key));
            }
        }

        return impacts;
    }

    private static IReadOnlyList<SeatSnapshot> BuildSeatSnapshot(IEnumerable<TripSeat> seats) =>
        seats.OrderBy(seat => seat.SeatNumber).ThenBy(seat => seat.Id)
            .Select(seat => new SeatSnapshot(seat.Id, seat.SeatNumber, seat.SeatType, seat.Status))
            .ToArray();

    private static IReadOnlyList<PendingTripSnapshot> BuildPendingTripSnapshot(
        IEnumerable<Domain.Entities.Trip> pendingTrips) =>
        pendingTrips.OrderBy(trip => trip.DepartureDateTime).ThenBy(trip => trip.Id)
            .Select(trip => new PendingTripSnapshot(
                trip.Id,
                trip.OperatorId,
                trip.RouteId,
                trip.VehicleId,
                trip.DriverUserId,
                trip.AssistantUserId,
                trip.DriverScheduleId,
                trip.DepartureDateTime,
                trip.EstimatedArrivalTime,
                trip.Status,
                trip.Source))
            .ToArray();

    private static void EnsureReplacementVehicleCanBeAssigned(Vehicle vehicle, Guid operatorId)
    {
        if (vehicle.OperatorId != operatorId
            || !vehicle.IsActive
            || vehicle.DeletedAt is not null
            || vehicle.Status != VehicleStatus.ACTIVE)
        {
            throw ValidationFailure("vehicleId", "Vehicle must be active and owned by the caller operator.");
        }
    }

    private static void EnsureUnchanged(ScheduleState expected, DriverSchedule actual)
    {
        if (!StatesEqual(expected, ScheduleState.From(actual)))
        {
            throw StaleConflict();
        }
    }

    private static bool StatesEqual(ScheduleState left, ScheduleState right) =>
        left.DepartureTime == right.DepartureTime
        && left.DayOfWeek.SequenceEqual(right.DayOfWeek)
        && left.DriverUserId == right.DriverUserId
        && left.AssistantUserId == right.AssistantUserId
        && left.VehicleId == right.VehicleId
        && left.ValidUntil == right.ValidUntil
        && left.IsActive == right.IsActive;

    private static bool IsInsideConfirmedBookingCutoff(
        Domain.Entities.Trip trip,
        ScheduleState effective,
        IReadOnlyCollection<string> changedFields,
        DateTimeOffset now)
    {
        if (trip.DepartureDateTime - now < TimeSpan.FromHours(2))
        {
            return true;
        }

        if (!changedFields.Contains("departureTime"))
        {
            return false;
        }

        var localDate = DateOnly.FromDateTime(trip.DepartureDateTime.ToOffset(IctOffset).DateTime);
        var newDeparture = BuildDepartureDateTime(localDate, effective.DepartureTime);
        return newDeparture - now < TimeSpan.FromHours(2);
    }

    private static DateTimeOffset BuildDepartureDateTime(DateOnly date, TimeOnly time) =>
        new DateTimeOffset(date.ToDateTime(time), IctOffset).ToUniversalTime();

    private static int ToContractDayOfWeek(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday
        ? 7
        : (int)date.DayOfWeek;

    private static IReadOnlyList<int> NormalizeDays(IEnumerable<int> days) =>
        days.Distinct().Order().ToArray();

    private static int PassengerRank(TripSeatType seatType) => seatType switch
    {
        TripSeatType.STANDARD => 0,
        TripSeatType.SLEEPER_UPPER => 1,
        TripSeatType.SLEEPER_LOWER => 2,
        TripSeatType.VIP => 3,
        _ => throw new CodedValidationException("VALIDATION_ERROR", "DRIVER_AREA is not a passenger seat type."),
    };

    private static string NormalizeSeatNumber(string seatNumber) => seatNumber.Trim().ToUpperInvariant();

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;

    private static CodedNotFoundException ScheduleNotFound() =>
        new("RESOURCE_NOT_FOUND", "Driver schedule was not found.");

    private static CodedConflictException StaleConflict() =>
        new("TRIP_NOT_EDITABLE", "DriverSchedule or linked Trip changed during preflight; retry.");

    private static ValidationException ValidationFailure(string field, string message) =>
        new(message, [new ValidationError(field, message)]);

    private sealed record ScheduleState(
        TimeOnly DepartureTime,
        IReadOnlyList<int> DayOfWeek,
        Guid DriverUserId,
        Guid? AssistantUserId,
        Guid? VehicleId,
        DateOnly? ValidUntil,
        bool IsActive)
    {
        public static ScheduleState From(DriverSchedule schedule) =>
            new(
                schedule.DepartureTime,
                NormalizeDays(schedule.DayOfWeek.EnumerateArray().Select(day => day.GetInt32())),
                schedule.DriverUserId,
                schedule.AssistantUserId,
                schedule.VehicleId,
                schedule.ValidUntil,
                schedule.IsActive);
    }

    private sealed record LockedTrip(
        Domain.Entities.Trip Trip,
        IReadOnlyList<TripSeat> Seats,
        IReadOnlyList<TripStop> Stops);

    private sealed record SeatSnapshot(Guid Id, string SeatNumber, TripSeatType Type, TripSeatStatus Status);

    private sealed record PendingTripSnapshot(
        Guid Id,
        Guid OperatorId,
        Guid RouteId,
        Guid VehicleId,
        Guid DriverUserId,
        Guid? AssistantUserId,
        Guid? DriverScheduleId,
        DateTimeOffset DepartureDateTime,
        DateTimeOffset EstimatedArrivalTime,
        TripStatus Status,
        TripSource Source);
}
