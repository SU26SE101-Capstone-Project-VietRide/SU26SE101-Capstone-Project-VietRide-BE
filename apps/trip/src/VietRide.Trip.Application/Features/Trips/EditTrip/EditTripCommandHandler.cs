using System.Text.Json;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.Trips.GetTripDetail;
using VietRide.Trip.Application.Features.Vehicles;
using VietRide.Trip.Domain.Constants;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Trips.EditTrip;

public sealed class EditTripCommandHandler : IRequestHandler<EditTripCommand, TripDetailDto>
{
    private const string RouteChangedEventType = "trip.trip.route_changed";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITripRepository trips;
    private readonly ITripSeatRepository tripSeats;
    private readonly ITripStopRepository tripStops;
    private readonly ITripStopFareRepository tripStopFares;
    private readonly IRouteRepository routes;
    private readonly IRouteStopRepository routeStops;
    private readonly IVehicleRepository vehicles;
    private readonly IBookingImpactClient bookingImpact;
    private readonly ITripVehicleSwapService vehicleSwap;
    private readonly ITripAuditLogRepository auditLogs;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IUnitOfWork unitOfWork;
    private readonly IClock clock;
    private readonly ISender sender;
    private readonly IRouteChangeProposalLifecycleService? routeChangeProposals;

    public EditTripCommandHandler(
        ITripRepository trips,
        ITripSeatRepository tripSeats,
        ITripStopRepository tripStops,
        ITripStopFareRepository tripStopFares,
        IRouteRepository routes,
        IRouteStopRepository routeStops,
        IVehicleRepository vehicles,
        IBookingImpactClient bookingImpact,
        ITripVehicleSwapService vehicleSwap,
        ITripAuditLogRepository auditLogs,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IClock clock,
        ISender sender,
        IRouteChangeProposalLifecycleService? routeChangeProposals = null)
    {
        this.trips = trips;
        this.tripSeats = tripSeats;
        this.tripStops = tripStops;
        this.tripStopFares = tripStopFares;
        this.routes = routes;
        this.routeStops = routeStops;
        this.vehicles = vehicles;
        this.bookingImpact = bookingImpact;
        this.vehicleSwap = vehicleSwap;
        this.auditLogs = auditLogs;
        this.outbox = outbox;
        this.unitOfWork = unitOfWork;
        this.clock = clock;
        this.sender = sender;
        this.routeChangeProposals = routeChangeProposals;
    }

    public async Task<TripDetailDto> Handle(EditTripCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var trip = await trips.GetByIdAsync(request.TripId, cancellationToken);
        if (trip is null || trip.OperatorId != request.OperatorId)
        {
            throw TripNotFound();
        }

        var normalizedNotes = NormalizeNotes(request.Notes);
        var changed = GetChangedFields(request, trip, normalizedNotes);
        if (changed.Count == 0)
        {
            return await GetDetailAsync(request.TripId, cancellationToken);
        }

        ValidateLifecycle(trip.Status, changed);
        var newRoute = await LoadRouteAsync(request, changed, cancellationToken);
        var newVehicle = await LoadVehicleAsync(request, changed, cancellationToken);
        var oldVehicle = changed.Contains(EditTripField.VehicleId)
            ? await vehicles.GetOwnedByIdAsync(request.OperatorId, trip.VehicleId, cancellationToken)
                ?? throw TripNotFound()
            : null;

        var currentSeats = changed.Contains(EditTripField.VehicleId) || changed.Contains(EditTripField.RouteId)
            ? tripSeats.QueryNoTracking()
                .Where(seat => seat.TripId == trip.Id)
                .OrderBy(seat => seat.SeatNumber)
                .ThenBy(seat => seat.Id)
                .ToArray()
            : [];
        var currentSeatSnapshot = BuildSeatSnapshot(currentSeats);
        var seatClassifications = newVehicle is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : ClassifySeats(currentSeats, newVehicle);

        TripBookingImpactProjection? projection = null;
        if (changed.Contains(EditTripField.RouteId) || changed.Contains(EditTripField.VehicleId))
        {
            projection = await bookingImpact.GetTripEditImpactAsync(
                trip.Id,
                request.OperatorId,
                cancellationToken);
        }

        ApplyConflictPrecedence(trip, changed, projection, currentSeats, seatClassifications, now);

        if (newVehicle is not null && await trips.HasVehicleConflictAsync(
                newVehicle.Id,
                trip.DepartureDateTime,
                trip.Id,
                cancellationToken))
        {
            throw new CodedConflictException("TRIP_VEHICLE_CONFLICT", "Vehicle is assigned to another Trip at this departure time.");
        }

        var originalState = new EditPreflightState(
            trip.BaseFare.Amount,
            trip.Notes,
            trip.VehicleId,
            trip.RouteId);

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var lockedTrip = await trips.AcquireForVehicleSwapAsync(trip.Id, cancellationToken);
            if (lockedTrip is null || lockedTrip.OperatorId != request.OperatorId)
            {
                throw TripNotFound();
            }

            var lockedChanged = GetChangedFields(request, lockedTrip, normalizedNotes);
            if (lockedChanged.Count == 0)
            {
                await unitOfWork.CommitAsync(cancellationToken);
            }
            else
            {
                // A field that was a no-op during preflight but became different before the lock
                // has not had its tenant references or Booking impact preflighted. Reject the stale
                // request instead of silently overwriting the concurrent value.
                if (lockedChanged.Any(field => !changed.Contains(field)))
                {
                    throw new CodedConflictException(
                        "TRIP_NOT_EDITABLE",
                        "Trip changed while the edit was being prepared; retry with the latest Trip state.");
                }

                if (HasDivergedFromPreflight(request, changed, lockedTrip, normalizedNotes, originalState))
                {
                    throw new CodedConflictException(
                        "TRIP_NOT_EDITABLE",
                        "Trip changed while the edit was being prepared; retry with the latest Trip state.");
                }

                ValidateLifecycle(lockedTrip.Status, lockedChanged);

                if (lockedChanged.Contains(EditTripField.RouteId) && routeChangeProposals is not null)
                    await routeChangeProposals.SupersedePendingAsync(lockedTrip.Id, request.ActorUserId, null, now, cancellationToken);

                IReadOnlyList<Vehicle> lockedVehicles = [];
                Vehicle? lockedOldVehicle = null;
                Vehicle? lockedNewVehicle = null;
                if (lockedChanged.Contains(EditTripField.VehicleId))
                {
                    lockedVehicles = await vehicles.AcquireForVehicleSwapAsync(
                        request.OperatorId,
                        [lockedTrip.VehicleId, request.VehicleId!.Value],
                        cancellationToken);
                    if (lockedVehicles.Count != 2)
                    {
                        throw new CodedNotFoundException("VEHICLE_NOT_FOUND", "Vehicle was not found.");
                    }

                    lockedOldVehicle = lockedVehicles.Single(vehicle => vehicle.Id == lockedTrip.VehicleId);
                    lockedNewVehicle = lockedVehicles.Single(vehicle => vehicle.Id == request.VehicleId.Value);
                    EnsureVehicleActive(lockedNewVehicle);
                }

                var lockedSeats = lockedChanged.Contains(EditTripField.VehicleId) || lockedChanged.Contains(EditTripField.RouteId)
                    ? await tripSeats.AcquireForVehicleSwapAsync(lockedTrip.Id, cancellationToken)
                    : [];
                if (!currentSeatSnapshot.SequenceEqual(BuildSeatSnapshot(lockedSeats)))
                {
                    throw StaleEditConflict();
                }

                var lockedStops = lockedChanged.Contains(EditTripField.RouteId) || lockedChanged.Contains(EditTripField.BaseFare)
                    ? await tripStops.AcquireByTripAsync(lockedTrip.Id, cancellationToken)
                    : [];
                var lockedFares = lockedChanged.Contains(EditTripField.RouteId) || lockedChanged.Contains(EditTripField.BaseFare)
                    ? await tripStopFares.AcquireByTripAsync(lockedTrip.Id, cancellationToken)
                    : [];

                Domain.Entities.Route? lockedRoute = null;
                IReadOnlyList<RouteStop> lockedRouteStops = [];
                if (lockedChanged.Contains(EditTripField.RouteId))
                {
                    lockedRoute = await routes.AcquireOwnedActiveAsync(
                        request.OperatorId,
                        request.RouteId!.Value,
                        cancellationToken)
                        ?? throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Route was not found.");
                    lockedRouteStops = await routeStops.AcquireByRouteAsync(lockedRoute.Id, cancellationToken);
                }

                ApplyConflictPrecedence(
                    lockedTrip,
                    lockedChanged,
                    projection,
                    lockedSeats,
                    lockedNewVehicle is null
                        ? new Dictionary<string, string?>(StringComparer.Ordinal)
                        : ClassifySeats(lockedSeats, lockedNewVehicle),
                    now);

                if (lockedChanged.Contains(EditTripField.RouteId)
                    && lockedSeats.Any(seat => seat.Status is TripSeatStatus.HELD or TripSeatStatus.BOOKED))
                {
                    throw RouteBookingConflict();
                }

                IReadOnlyCollection<VehicleSwapBookingSeatImpact> lockedImpacts = [];
                if (lockedNewVehicle is not null)
                {
                    var lockedClassifications = ClassifySeats(lockedSeats, lockedNewVehicle);
                    lockedImpacts = BuildBookingSeatImpacts(projection, lockedSeats, lockedClassifications);
                    if (await trips.HasVehicleConflictAsync(
                            lockedNewVehicle.Id,
                            lockedTrip.DepartureDateTime,
                            lockedTrip.Id,
                            cancellationToken))
                    {
                        throw new CodedConflictException(
                            "TRIP_VEHICLE_CONFLICT",
                            "Vehicle is assigned to another Trip at this departure time.");
                    }
                }

                await ApplyChangesAsync(
                    request,
                    lockedTrip,
                    lockedChanged,
                    normalizedNotes,
                    lockedRoute,
                    lockedRouteStops,
                    lockedStops,
                    lockedFares,
                    lockedOldVehicle,
                    lockedNewVehicle,
                    lockedVehicles,
                    lockedSeats,
                    lockedImpacts,
                    now,
                    cancellationToken);

                await unitOfWork.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        return await GetDetailAsync(request.TripId, cancellationToken);
    }

    private async Task ApplyChangesAsync(
        EditTripCommand request,
        Domain.Entities.Trip trip,
        IReadOnlySet<EditTripField> changed,
        string? normalizedNotes,
        Domain.Entities.Route? newRoute,
        IReadOnlyList<RouteStop> newRouteStops,
        IReadOnlyList<TripStop> lockedStops,
        IReadOnlyList<TripStopFare> lockedFares,
        Vehicle? oldVehicle,
        Vehicle? newVehicle,
        IReadOnlyList<Vehicle> lockedVehicles,
        IReadOnlyList<TripSeat> lockedSeats,
        IReadOnlyCollection<VehicleSwapBookingSeatImpact> impacts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var scalarFields = new List<string>();
        var scalarBefore = new Dictionary<string, object?>();
        var scalarAfter = new Dictionary<string, object?>();

        if (changed.Contains(EditTripField.RouteId) && newRoute is not null)
        {
            var beforeRouteId = trip.RouteId;
            var estimatedArrival = trip.DepartureDateTime.AddMinutes(
                ResolveEstimatedTripDuration(newRoute, newRouteStops));

            trip.ChangeRoute(newRoute.Id, estimatedArrival);
            await tripStopFares.DeleteByTripAsync(trip.Id, cancellationToken);
            await tripStops.DeleteByTripAsync(trip.Id, cancellationToken);
            foreach (var routeStop in newRouteStops)
            {
                await tripStops.AddAsync(
                    TripStop.Create(
                        trip.Id,
                        routeStop.StopId,
                        routeStop.OrderIndex,
                        trip.DepartureDateTime.AddMinutes(routeStop.EstimatedDurationFromOriginMinutes),
                        routeStop.AllowPickup,
                        routeStop.AllowDropoff,
                        routeStop.DistanceFromOriginKm),
                    cancellationToken);
            }

            await AddAuditAsync(
                trip.Id,
                request.ActorUserId,
                TripAuditAction.TripRouteChanged,
                ["routeId"],
                new { routeId = beforeRouteId },
                new { routeId = newRoute.Id },
                request.RequestId,
                now,
                cancellationToken);
            await outbox.EnqueueAsync(
                RouteChangedEventType,
                JsonSerializer.Serialize(new
                {
                    tripId = trip.Id,
                    alternativeRouteId = newRoute.Id,
                    affectedBookingIds = Array.Empty<Guid>(),
                }, JsonOptions),
                cancellationToken);
        }

        if (changed.Contains(EditTripField.BaseFare))
        {
            var beforeFare = trip.BaseFare.Amount;
            var fare = Money.FromRaw(request.BaseFare!.Value);
            trip.ChangeBaseFare(fare);

            var targetStopIds = newRoute is null
                ? lockedStops.Select(stop => stop.StopId).ToArray()
                : newRouteStops.Select(stop => stop.StopId).ToArray();
            if (newRoute is null)
            {
                foreach (var existing in lockedFares)
                {
                    existing.ChangeFare(fare, TripStopFareSource.MANUAL_OVERRIDE);
                }
            }

            var existingManualStopIds = lockedFares
                .Where(_ => newRoute is null)
                .Select(item => item.StopId)
                .ToHashSet();
            foreach (var stopId in targetStopIds.Where(stopId => !existingManualStopIds.Contains(stopId)))
            {
                await tripStopFares.AddAsync(
                    TripStopFare.Create(trip.Id, stopId, fare, TripStopFareSource.MANUAL_OVERRIDE),
                    cancellationToken);
            }

            scalarFields.Add("baseFare");
            scalarBefore["baseFare"] = beforeFare;
            scalarAfter["baseFare"] = fare.Amount;
        }

        if (changed.Contains(EditTripField.Notes))
        {
            var beforeNotes = trip.Notes;
            trip.UpdateNotes(normalizedNotes);
            scalarFields.Add("notes");
            scalarBefore["notes"] = beforeNotes;
            scalarAfter["notes"] = normalizedNotes;
        }

        if (scalarFields.Count > 0)
        {
            await AddAuditAsync(
                trip.Id,
                request.ActorUserId,
                TripAuditAction.TripEdited,
                scalarFields,
                scalarBefore,
                scalarAfter,
                request.RequestId,
                now,
                cancellationToken);
        }

        if (changed.Contains(EditTripField.VehicleId) && oldVehicle is not null && newVehicle is not null)
        {
            await vehicleSwap.StageSwapAsync(
                trip,
                lockedVehicles.Single(vehicle => vehicle.Id == oldVehicle.Id),
                lockedVehicles.Single(vehicle => vehicle.Id == newVehicle.Id),
                lockedSeats,
                impacts,
                request.ActorUserId,
                TripAuditAction.TripVehicleSwapped,
                request.RequestId,
                now,
                cancellationToken);
        }
    }

    private static IReadOnlySet<EditTripField> GetChangedFields(
        EditTripCommand request,
        Domain.Entities.Trip trip,
        string? normalizedNotes)
    {
        var changed = new HashSet<EditTripField>();
        if (request.BaseFareSpecified && request.BaseFare != trip.BaseFare.Amount)
        {
            changed.Add(EditTripField.BaseFare);
        }

        if (request.NotesSpecified && !string.Equals(normalizedNotes, trip.Notes, StringComparison.Ordinal))
        {
            changed.Add(EditTripField.Notes);
        }

        if (request.VehicleIdSpecified && request.VehicleId != trip.VehicleId)
        {
            changed.Add(EditTripField.VehicleId);
        }

        if (request.RouteIdSpecified && request.RouteId != trip.RouteId)
        {
            changed.Add(EditTripField.RouteId);
        }

        return changed;
    }

    private static bool HasDivergedFromPreflight(
        EditTripCommand request,
        IReadOnlySet<EditTripField> preflightChanged,
        Domain.Entities.Trip lockedTrip,
        string? normalizedNotes,
        EditPreflightState original) =>
        preflightChanged.Contains(EditTripField.BaseFare)
            && lockedTrip.BaseFare.Amount != original.BaseFare
            && lockedTrip.BaseFare.Amount != request.BaseFare
        || preflightChanged.Contains(EditTripField.Notes)
            && !string.Equals(lockedTrip.Notes, original.Notes, StringComparison.Ordinal)
            && !string.Equals(lockedTrip.Notes, normalizedNotes, StringComparison.Ordinal)
        || preflightChanged.Contains(EditTripField.VehicleId)
            && lockedTrip.VehicleId != original.VehicleId
            && lockedTrip.VehicleId != request.VehicleId
        || preflightChanged.Contains(EditTripField.RouteId)
            && lockedTrip.RouteId != original.RouteId
            && lockedTrip.RouteId != request.RouteId;

    private static IReadOnlyList<SeatSnapshot> BuildSeatSnapshot(IReadOnlyCollection<TripSeat> seats) =>
        seats
            .Select(seat => new SeatSnapshot(
                seat.Id,
                NormalizeSeatNumber(seat.SeatNumber),
                seat.Status,
                seat.SeatType))
            .OrderBy(seat => seat.SeatNumber, StringComparer.Ordinal)
            .ThenBy(seat => seat.Id)
            .ToArray();

    private static CodedConflictException StaleEditConflict() =>
        new(
            "TRIP_NOT_EDITABLE",
            "Trip changed while the edit was being prepared; retry with the latest Trip state.");

    private static int ResolveEstimatedTripDuration(
        Domain.Entities.Route route,
        IReadOnlyCollection<RouteStop> routeStops)
    {
        if (route.EstimatedDurationMinutes is > 0)
        {
            return route.EstimatedDurationMinutes.Value;
        }

        var fallback = routeStops.Count == 0
            ? 0
            : routeStops.Max(routeStop => routeStop.EstimatedDurationFromOriginMinutes);
        if (fallback > 0)
        {
            return fallback;
        }

        throw new ValidationException(
            "Route estimated duration is required for trip generation.",
            [new ValidationError("estimatedArrivalTime", "Route duration or route-stop duration is required.")]);
    }

    private static void ValidateLifecycle(TripStatus status, IReadOnlySet<EditTripField> changed)
    {
        var allowed = changed.All(field => field switch
        {
            EditTripField.BaseFare or EditTripField.RouteId => status == TripStatus.SCHEDULED,
            EditTripField.VehicleId => status is TripStatus.SCHEDULED or TripStatus.BOARDING,
            EditTripField.Notes => status is TripStatus.SCHEDULED or TripStatus.BOARDING or TripStatus.IN_PROGRESS,
            _ => false,
        });
        if (!allowed)
        {
            throw new CodedConflictException("TRIP_NOT_EDITABLE", "One or more requested fields are not editable in the current Trip status.");
        }
    }

    private async Task<Domain.Entities.Route?> LoadRouteAsync(
        EditTripCommand request,
        IReadOnlySet<EditTripField> changed,
        CancellationToken cancellationToken)
    {
        if (!changed.Contains(EditTripField.RouteId))
        {
            return null;
        }

        return await routes.GetOwnedActiveByIdAsync(request.OperatorId, request.RouteId!.Value, cancellationToken)
            ?? throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Route was not found.");
    }

    private async Task<Vehicle?> LoadVehicleAsync(
        EditTripCommand request,
        IReadOnlySet<EditTripField> changed,
        CancellationToken cancellationToken)
    {
        if (!changed.Contains(EditTripField.VehicleId))
        {
            return null;
        }

        var vehicle = await vehicles.GetOwnedByIdAsync(
            request.OperatorId,
            request.VehicleId!.Value,
            cancellationToken)
            ?? throw new CodedNotFoundException("VEHICLE_NOT_FOUND", "Vehicle was not found.");
        EnsureVehicleActive(vehicle);

        return vehicle;
    }

    private static void ApplyConflictPrecedence(
        Domain.Entities.Trip trip,
        IReadOnlySet<EditTripField> changed,
        TripBookingImpactProjection? projection,
        IReadOnlyCollection<TripSeat> seats,
        IReadOnlyDictionary<string, string?> classifications,
        DateTimeOffset now)
    {
        if (changed.Contains(EditTripField.RouteId) && projection is { ActiveBookingCount: > 0 })
        {
            throw RouteBookingConflict();
        }

        if (changed.Contains(EditTripField.VehicleId))
        {
            ApplyVehicleConflicts(trip, seats, classifications, now);
        }
    }

    private static void ApplyVehicleConflicts(
        Domain.Entities.Trip trip,
        IReadOnlyCollection<TripSeat> seats,
        IReadOnlyDictionary<string, string?> classifications,
        DateTimeOffset now)
    {
        var incompatibleHeld = seats.Any(seat =>
            seat.Status == TripSeatStatus.HELD && classifications.GetValueOrDefault(seat.SeatNumber) is not null);
        var incompatibleBooked = seats.Any(seat =>
            seat.Status == TripSeatStatus.BOOKED && classifications.GetValueOrDefault(seat.SeatNumber) is not null);

        if (trip.Status == TripStatus.BOARDING && (incompatibleHeld || incompatibleBooked))
        {
            throw new CodedConflictException(
                "TRIP_VEHICLE_SWAP_TOO_LATE",
                "Vehicle swap is too late for an incompatible passenger seat.");
        }

        if (trip.Status == TripStatus.SCHEDULED && incompatibleHeld)
        {
            throw new CodedConflictException(
                "TRIP_VEHICLE_SWAP_HELD_SEAT_CONFLICT",
                "Vehicle swap would make a held passenger seat incompatible.");
        }

        var deadline = Min(now.AddHours(4), trip.DepartureDateTime.AddMinutes(-30));
        if (incompatibleBooked && (trip.Status == TripStatus.BOARDING || deadline <= now))
        {
            throw new CodedConflictException(
                "TRIP_VEHICLE_SWAP_TOO_LATE",
                "Vehicle swap is too late for an incompatible booked passenger seat.");
        }
    }

    private static IReadOnlyCollection<VehicleSwapBookingSeatImpact> BuildBookingSeatImpacts(
        TripBookingImpactProjection? projection,
        IReadOnlyCollection<TripSeat> seats,
        IReadOnlyDictionary<string, string?> classifications)
    {
        if (projection is null)
        {
            return [];
        }

        var bookedSeats = seats
            .Where(seat => seat.Status == TripSeatStatus.BOOKED)
            .ToDictionary(seat => seat.SeatNumber, StringComparer.Ordinal);
        var impacts = new List<VehicleSwapBookingSeatImpact>();
        foreach (var booking in projection.ActiveBookings.OrderBy(booking => booking.BookingId))
        {
            var grouped = booking.SeatNumbers
                .Select(NormalizeSeatNumber)
                .Where(bookedSeats.ContainsKey)
                .Select(seatNumber => new { SeatNumber = seatNumber, Reason = classifications.GetValueOrDefault(seatNumber) })
                .Where(item => item.Reason is not null)
                .GroupBy(item => item.Reason!, StringComparer.Ordinal);
            foreach (var group in grouped)
            {
                impacts.Add(new VehicleSwapBookingSeatImpact(
                    booking.BookingId,
                    group.Select(item => item.SeatNumber).ToArray(),
                    group.Key));
            }
        }

        return impacts;
    }

    private static IReadOnlyDictionary<string, string?> ClassifySeats(
        IReadOnlyCollection<TripSeat> seats,
        Vehicle newVehicle)
    {
        var layout = newVehicle.SeatLayoutJson.Deserialize<SeatLayoutDto>(JsonOptions)
            ?? throw new CodedValidationException("VALIDATION_ERROR", "Vehicle seat layout is invalid.");
        var newSeats = layout.Seats.ToDictionary(
            seat => NormalizeSeatNumber(seat.SeatNumber),
            seat => seat,
            StringComparer.Ordinal);
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var oldSeat in seats.Where(seat => seat.Status is TripSeatStatus.HELD or TripSeatStatus.BOOKED))
        {
            if (!newSeats.TryGetValue(oldSeat.SeatNumber, out var newSeat))
            {
                result[oldSeat.SeatNumber] = VehicleSwapBookingSeatImpact.SeatRemoved;
                continue;
            }

            if (!Enum.TryParse<TripSeatType>(newSeat.Type, true, out var newType) || !Enum.IsDefined(newType))
            {
                throw new CodedValidationException("VALIDATION_ERROR", "Vehicle seat layout contains an unknown seat type.");
            }

            if (newSeat.Disabled || newType == TripSeatType.DRIVER_AREA)
            {
                result[oldSeat.SeatNumber] = VehicleSwapBookingSeatImpact.SeatDisabled;
                continue;
            }

            result[oldSeat.SeatNumber] = PassengerRank(newType) < PassengerRank(oldSeat.SeatType)
                ? VehicleSwapBookingSeatImpact.SeatTypeDowngraded
                : null;
        }

        return result;
    }

    private async Task AddAuditAsync(
        Guid tripId,
        Guid actorUserId,
        string action,
        IReadOnlyList<string> changedFields,
        object before,
        object after,
        string requestId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        await auditLogs.AddAsync(
            TripAuditLog.Create(
                Guid.NewGuid(),
                tripId,
                actorUserId,
                action,
                JsonSerializer.Serialize(new { changedFields, before, after, requestId }, JsonOptions),
                occurredAt),
            cancellationToken);

    private Task<TripDetailDto> GetDetailAsync(Guid tripId, CancellationToken cancellationToken) =>
        sender.Send(new GetTripDetailQuery(tripId), cancellationToken);

    private static string? NormalizeNotes(string? notes)
    {
        var normalized = notes?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static string NormalizeSeatNumber(string seatNumber) => seatNumber.Trim().ToUpperInvariant();

    private static int PassengerRank(TripSeatType seatType) => seatType switch
    {
        TripSeatType.STANDARD => 0,
        TripSeatType.SLEEPER_UPPER => 1,
        TripSeatType.SLEEPER_LOWER => 2,
        TripSeatType.VIP => 3,
        _ => throw new CodedValidationException("VALIDATION_ERROR", "DRIVER_AREA is not a passenger seat type."),
    };

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;

    private static CodedNotFoundException TripNotFound() =>
        new("TRIP_NOT_FOUND", "Trip was not found.");

    private static void EnsureVehicleActive(Vehicle vehicle)
    {
        if (!vehicle.IsActive || vehicle.DeletedAt is not null || vehicle.Status != VehicleStatus.ACTIVE)
        {
            throw new CodedValidationException("VEHICLE_NOT_ACTIVE", "Vehicle must be active.");
        }
    }

    private static CodedConflictException RouteBookingConflict() =>
        new("TRIP_ROUTE_CHANGE_BOOKINGS_EXIST", "Route cannot be changed while active Bookings exist.");

    private sealed record EditPreflightState(
        long BaseFare,
        string? Notes,
        Guid VehicleId,
        Guid RouteId);

    private sealed record SeatSnapshot(
        Guid Id,
        string SeatNumber,
        TripSeatStatus Status,
        TripSeatType SeatType);
}
