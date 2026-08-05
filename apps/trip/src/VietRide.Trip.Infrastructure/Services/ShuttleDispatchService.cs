using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.Stops;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Services;

internal sealed class ShuttleDispatchService : IShuttleDispatchService
{
    private const int ArrivalBufferMinutes = 30;
    private const int DefaultShuttleMaxDistanceKm = 5;
    private readonly TripDbContext _db;
    private readonly IIdentityInternalClient _identity;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;
    private readonly int _maxShuttleDistanceMeters;

    public ShuttleDispatchService(
        TripDbContext db,
        IIdentityInternalClient identity,
        IIntegrationEventOutbox outbox,
        IClock clock,
        IConfiguration configuration)
    {
        _db = db;
        _identity = identity;
        _outbox = outbox;
        _clock = clock;
        var maxDistanceKm = configuration.GetValue<int?>("SHUTTLE_MAX_DISTANCE_KM") ?? DefaultShuttleMaxDistanceKm;
        _maxShuttleDistanceMeters = checked(Math.Max(1, maxDistanceKm) * 1_000);
    }

    public async Task<ShuttleRequestPage> GetPendingAsync(
        Guid operatorId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var tripDirections = await _db.ShuttlePassengers.AsNoTracking()
            .Where(passenger => passenger.Status == ShuttlePassenger.PendingAssignmentStatus)
            .Join(_db.Trips.AsNoTracking().Where(trip => trip.OperatorId == operatorId),
                passenger => passenger.MainTripId,
                trip => trip.Id,
                (passenger, trip) => new { trip.Id, passenger.Direction })
            .Distinct()
            .OrderBy(item => item.Id)
            .ThenBy(item => item.Direction)
            .ToArrayAsync(cancellationToken);

        var pagedDirections = tripDirections.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        var items = new List<ShuttleRequestTripGroup>();
        foreach (var item in pagedDirections)
        {
            var trip = await _db.Trips.AsNoTracking().SingleAsync(x => x.Id == item.Id, cancellationToken);
            var route = await _db.Routes.AsNoTracking().SingleAsync(x => x.Id == trip.RouteId, cancellationToken);
            var stationId = item.Direction == ShuttleTrip.InboundDirection
                ? route.OriginStationId
                : route.DestinationStationId;
            var station = await _db.Stations.AsNoTracking().SingleAsync(x => x.Id == stationId, cancellationToken);
            var manifests = await _db.ShuttlePassengers.AsNoTracking()
                .Where(x => x.MainTripId == item.Id
                    && x.Direction == item.Direction
                    && x.Status == ShuttlePassenger.PendingAssignmentStatus)
                .ToArrayAsync(cancellationToken);
            var groups = manifests
                .Where(x => x.BookingId.HasValue)
                .GroupBy(x => x.BookingId!.Value)
                .Select(group =>
                {
                    var first = group.OrderBy(x => x.CreatedAt).First();
                    return new ShuttleBookingGroup(
                        group.Key,
                        group.Count(),
                        first.PickupAddress,
                        first.PickupLat,
                        first.PickupLng,
                        first.RoadDistanceMeters,
                        first.CreatedAt,
                        first.RoadDistanceMeters);
                })
                .OrderBy(group => group.RequestedAt)
                .ToArray();
            var suggested = groups
                .OrderByDescending(group => group.DistanceToStationMeters)
                .ThenBy(group => group.RequestedAt)
                .Select(group => group.BookingId)
                .ToArray();
            items.Add(new ShuttleRequestTripGroup(
                trip.Id,
                item.Direction,
                trip.DepartureDateTime,
                item.Direction == ShuttleTrip.InboundDirection
                    ? trip.DepartureDateTime.AddMinutes(-ArrivalBufferMinutes)
                    : trip.EstimatedArrivalTime.AddMinutes(ArrivalBufferMinutes),
                station.Id,
                station.Name,
                manifests.Length,
                groups,
                suggested));
        }

        return new ShuttleRequestPage(items, page, pageSize, tripDirections.Length);
    }

    public async Task<CreateShuttleTripResult> CreateAsync(
        CreateShuttleTripInput input,
        CancellationToken cancellationToken)
    {
        if (input.OrderedBookingIds.Count == 0
            || input.OrderedBookingIds.Any(id => id == Guid.Empty)
            || input.OrderedBookingIds.Distinct().Count() != input.OrderedBookingIds.Count)
        {
            throw new CodedValidationException("VALIDATION_ERROR", "orderedBookingIds must be a non-empty distinct list.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await AcquireLockAsync("driver", input.DriverUserId, cancellationToken);
        await AcquireLockAsync("vehicle", input.VehicleId, cancellationToken);

        var trip = await _db.Trips.SingleOrDefaultAsync(
            x => x.Id == input.MainTripId && x.OperatorId == input.OperatorId,
            cancellationToken) ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Main trip was not found.");
        if (trip.Status != TripStatus.SCHEDULED)
        {
            throw new ConflictException("BOOKING_TRIP_NOT_BOOKABLE", "Main trip is not scheduled.");
        }

        var isInbound = input.Direction == ShuttleTrip.InboundDirection;
        if (input.Direction is not (ShuttleTrip.InboundDirection or ShuttleTrip.OutboundDirection))
        {
            throw new CodedValidationException("VALIDATION_ERROR", "Shuttle direction is invalid.");
        }

        var scheduleBoundaryAt = isInbound
            ? trip.DepartureDateTime.AddMinutes(-ArrivalBufferMinutes)
            : trip.EstimatedArrivalTime.AddMinutes(ArrivalBufferMinutes);
        if (isInbound && _clock.UtcNow >= scheduleBoundaryAt)
        {
            throw new ConflictException(
                "SHUTTLE_REQUEST_CUTOFF_PASSED",
                "The shuttle dispatch cutoff has passed.");
        }

        if (input.ScheduledEndTime <= input.ScheduledDepartureTime
            || (isInbound && input.ScheduledEndTime > scheduleBoundaryAt)
            || (!isInbound && input.ScheduledDepartureTime < scheduleBoundaryAt))
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                isInbound
                    ? "Shuttle schedule violates the main-trip departure buffer."
                    : "Shuttle schedule must start at least 30 minutes after estimated arrival.");
        }

        var vehicle = await _db.Vehicles.SingleOrDefaultAsync(
            x => x.Id == input.VehicleId && x.OperatorId == input.OperatorId && x.IsActive,
            cancellationToken) ?? throw new CodedNotFoundException("VEHICLE_NOT_FOUND", "Vehicle was not found.");
        if (vehicle.Status != VehicleStatus.ACTIVE)
        {
            throw new ConflictException("SHUTTLE_VEHICLE_CONFLICT", "Vehicle is not active.");
        }

        var driver = await _identity.GetUserAsync(input.DriverUserId, cancellationToken);
        if (!driver.Found || driver.OperatorId != input.OperatorId
            || !string.Equals(driver.Role, "DRIVER", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(driver.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(driver.DisplayName)
            || string.IsNullOrWhiteSpace(driver.Phone))
        {
            throw new CodedNotFoundException("DRIVER_NOT_FOUND", "An active driver with contact details was not found.");
        }

        if (await HasDriverConflictAsync(input, cancellationToken))
        {
            throw new ConflictException("SHUTTLE_DRIVER_CONFLICT", "Driver has an overlapping trip.");
        }

        if (await HasVehicleConflictAsync(input, cancellationToken))
        {
            throw new ConflictException("SHUTTLE_VEHICLE_CONFLICT", "Vehicle has an overlapping trip.");
        }

        var selectedIds = input.OrderedBookingIds.ToArray();
        var manifests = await _db.ShuttlePassengers
            .FromSqlInterpolated($"SELECT * FROM vietride_trip.shuttle_passengers WHERE main_trip_id = {input.MainTripId} AND direction = {input.Direction} AND booking_id = ANY({selectedIds}) FOR UPDATE")
            .ToArrayAsync(cancellationToken);
        var grouped = manifests
            .Where(x => x.BookingId.HasValue)
            .GroupBy(x => x.BookingId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());
        if (selectedIds.Any(id => !grouped.TryGetValue(id, out var group)
                || group.Length == 0
                || group.Any(x => x.Status != ShuttlePassenger.PendingAssignmentStatus)))
        {
            throw new ConflictException("SHUTTLE_REQUEST_SET_CHANGED", "One or more selected Booking groups changed.");
        }

        // Recheck after the manifest lock so a request waiting behind cutoff cannot dispatch stale work.
        if (isInbound && _clock.UtcNow >= scheduleBoundaryAt)
        {
            throw new ConflictException(
                "SHUTTLE_REQUEST_CUTOFF_PASSED",
                "The shuttle dispatch cutoff has passed.");
        }

        if (manifests.Length > vehicle.TotalSeats)
        {
            throw new ConflictException("SHUTTLE_CAPACITY_EXCEEDED", "Selected passengers exceed vehicle capacity.");
        }

        if (manifests.Any(manifest => manifest.RoadDistanceMeters is null))
        {
            throw new CodedValidationException(
                "SHUTTLE_DISTANCE_UNAVAILABLE",
                "A selected shuttle manifest has no road-distance snapshot.");
        }

        if (manifests.Any(manifest => manifest.RoadDistanceMeters > _maxShuttleDistanceMeters))
        {
            throw new CodedValidationException(
                "SHUTTLE_DISTANCE_EXCEEDED",
                $"A selected shuttle manifest exceeds the {_maxShuttleDistanceMeters}m road-distance limit.");
        }

        var route = await _db.Routes.AsNoTracking().SingleAsync(x => x.Id == trip.RouteId, cancellationToken);
        var shuttleTrip = ShuttleTrip.Create(
            input.OperatorId,
            input.MainTripId,
            isInbound ? route.OriginStationId : route.DestinationStationId,
            input.DriverUserId,
            input.VehicleId,
            input.ScheduledDepartureTime,
            input.ScheduledEndTime,
            input.Notes,
            input.Direction);
        _db.ShuttleTrips.Add(shuttleTrip);

        for (var index = 0; index < selectedIds.Length; index++)
        {
            var bookingId = selectedIds[index];
            var bookingManifests = grouped[bookingId];
            foreach (var manifest in bookingManifests)
            {
                manifest.Assign(shuttleTrip.Id, index + 1);
            }

            var passengerUserId = bookingManifests.Select(x => x.PassengerUserId).FirstOrDefault(x => x.HasValue);
            await _outbox.EnqueueAsync("trip.shuttle.assigned", JsonSerializer.Serialize(new
            {
                eventId = Guid.NewGuid(),
                shuttleTripId = shuttleTrip.Id,
                mainTripId = input.MainTripId,
                operatorId = input.OperatorId,
                bookingId,
                passengerUserId,
                direction = input.Direction,
                ticketIds = bookingManifests.Select(x => x.TicketId).Where(x => x.HasValue).Select(x => x!.Value).ToArray(),
                pickupOrder = index + 1,
                scheduledDepartureTime = input.ScheduledDepartureTime,
                scheduledEndTime = input.ScheduledEndTime,
                driver = new { userId = input.DriverUserId, displayName = driver.DisplayName, phone = driver.Phone },
                vehicle = new { id = vehicle.Id, licensePlate = vehicle.LicensePlate },
            }), cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        var remaining = await _db.ShuttlePassengers.CountAsync(
            x => x.MainTripId == input.MainTripId
                && x.Direction == input.Direction
                && x.Status == ShuttlePassenger.PendingAssignmentStatus,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CreateShuttleTripResult(shuttleTrip.Id, input.MainTripId, manifests.Length, remaining);
    }

    public async Task<ShuttleDriverAssignmentPage> GetDriverAssignmentsAsync(
        Guid driverUserId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var (resolvedFrom, resolvedTo) = ResolveDriverDateRange(from, to);
        var startUtc = ToUtcBoundary(resolvedFrom);
        var endExclusiveUtc = ToUtcBoundary(resolvedTo.AddDays(1));
        var rows = await _db.ShuttleTrips.AsNoTracking()
            .Where(trip => trip.DriverUserId == driverUserId
                && trip.Status != ShuttleTrip.CancelledStatus
                && trip.ScheduledDepartureTime >= startUtc
                && trip.ScheduledDepartureTime < endExclusiveUtc)
            .Join(
                _db.Vehicles.AsNoTracking(),
                shuttleTrip => shuttleTrip.VehicleId,
                vehicle => vehicle.Id,
                (shuttleTrip, vehicle) => new
                {
                    ShuttleTrip = shuttleTrip,
                    vehicle.LicensePlate,
                })
            .Select(row => new
            {
                row.ShuttleTrip,
                row.LicensePlate,
                PassengerCount = _db.ShuttlePassengers.Count(
                    passenger => passenger.ShuttleTripId == row.ShuttleTrip.Id),
                StopCount = _db.ShuttlePassengers
                    .Where(passenger => passenger.ShuttleTripId == row.ShuttleTrip.Id
                        && passenger.PickupOrder.HasValue)
                    .Select(passenger => passenger.PickupOrder)
                    .Distinct()
                    .Count(),
            })
            .OrderBy(row => row.ShuttleTrip.ScheduledDepartureTime)
            .ThenBy(row => row.ShuttleTrip.Id)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new ShuttleDriverAssignment(
                row.ShuttleTrip.Id,
                row.ShuttleTrip.MainTripId,
                row.ShuttleTrip.Direction,
                row.ShuttleTrip.Status,
                row.ShuttleTrip.VehicleId,
                row.LicensePlate,
                row.ShuttleTrip.ScheduledDepartureTime,
                row.ShuttleTrip.ScheduledEndTime,
                row.PassengerCount,
                row.StopCount))
            .ToArray();

        return new ShuttleDriverAssignmentPage(resolvedFrom, resolvedTo, items);
    }

    public async Task<ShuttleDriverManifest> GetDriverManifestAsync(
        Guid shuttleTripId,
        Guid driverUserId,
        CancellationToken cancellationToken)
    {
        var shuttleTrip = await _db.ShuttleTrips.AsNoTracking()
            .SingleOrDefaultAsync(trip => trip.Id == shuttleTripId, cancellationToken)
            ?? throw new CodedNotFoundException("SHUTTLE_TRIP_NOT_FOUND", "Shuttle trip was not found.");
        if (shuttleTrip.DriverUserId != driverUserId)
        {
            throw new ForbiddenException("FORBIDDEN", "Shuttle trip is not assigned to this driver.");
        }

        var station = await _db.Stations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == shuttleTrip.StationId, cancellationToken)
            ?? throw new CodedNotFoundException("SHUTTLE_STATION_NOT_FOUND", "Shuttle station was not found.");
        var manifests = await _db.ShuttlePassengers.AsNoTracking()
            .Where(passenger => passenger.ShuttleTripId == shuttleTripId && passenger.PickupOrder.HasValue)
            .OrderBy(passenger => passenger.PickupOrder)
            .ThenBy(passenger => passenger.CreatedAt)
            .ToListAsync(cancellationToken);
        var passengerUserIds = manifests
            .Where(passenger => passenger.PassengerUserId.HasValue)
            .Select(passenger => passenger.PassengerUserId!.Value)
            .Distinct()
            .ToArray();
        var profiles = await _identity.GetUsersAsync(passengerUserIds, cancellationToken);

        var stops = manifests
            .GroupBy(passenger => new { passenger.PickupOrder, passenger.BookingId })
            .OrderBy(group => group.Key.PickupOrder)
            .Select(group =>
            {
                var first = group.First();
                var profile = first.PassengerUserId.HasValue
                    && profiles.TryGetValue(first.PassengerUserId.Value, out var foundProfile)
                    ? foundProfile
                    : null;
                return new ShuttleDriverManifestStop(
                    group.Key.PickupOrder!.Value,
                    group.Key.BookingId,
                    group.Where(passenger => passenger.TicketId.HasValue)
                        .Select(passenger => passenger.TicketId!.Value)
                        .Distinct()
                        .ToArray(),
                    group.Count(),
                    first.PickupAddress,
                    first.PickupLat,
                    first.PickupLng,
                    ResolveManifestGroupStatus(group.Select(passenger => passenger.Status)),
                    group.Where(passenger => passenger.PickedUpAt.HasValue)
                        .Select(passenger => passenger.PickedUpAt)
                        .Min(),
                    group.Where(passenger => passenger.DeliveredAt.HasValue)
                        .Select(passenger => passenger.DeliveredAt)
                        .Max(),
                    profile?.DisplayName,
                    profile?.Phone);
            })
            .ToArray();

        return new ShuttleDriverManifest(
            shuttleTrip.Id,
            shuttleTrip.MainTripId,
            shuttleTrip.Direction,
            shuttleTrip.Status,
            station.Id,
            station.Name,
            station.Latitude,
            station.Longitude,
            shuttleTrip.ScheduledDepartureTime,
            shuttleTrip.ScheduledEndTime,
            stops);
    }

    private static string ResolveManifestGroupStatus(IEnumerable<string> statuses)
    {
        var distinctStatuses = statuses.Distinct(StringComparer.Ordinal).ToArray();
        if (distinctStatuses.Length != 1)
        {
            throw new CodedConflictException(
                "SHUTTLE_MANIFEST_INCONSISTENT_STATUS",
                "Passengers in the same Shuttle pickup group must share one status.");
        }

        return distinctStatuses[0];
    }

    private static (DateOnly From, DateOnly To) ResolveDriverDateRange(DateOnly? from, DateOnly? to)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7).Date);
        var resolvedFrom = from ?? today;
        var resolvedTo = to ?? today.AddDays(14);
        if (resolvedTo < resolvedFrom)
        {
            throw new CodedValidationException("VALIDATION_ERROR", "The end date must not be before the start date.");
        }
        if (resolvedTo.DayNumber - resolvedFrom.DayNumber > 31)
        {
            throw new CodedValidationException("VALIDATION_ERROR", "The date range must not exceed 32 days.");
        }
        return (resolvedFrom, resolvedTo);
    }

    private static DateTimeOffset ToUtcBoundary(DateOnly date) =>
        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime();

    public async Task<ShuttleTrackingContext> GetTrackingContextAsync(
        Guid shuttleTripId,
        Guid userId,
        string role,
        Guid? operatorId,
        CancellationToken cancellationToken)
    {
        var shuttleTrip = await _db.ShuttleTrips.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == shuttleTripId, cancellationToken)
            ?? throw new CodedNotFoundException("SHUTTLE_TRIP_NOT_FOUND", "Shuttle trip was not found.");
        var manifests = await _db.ShuttlePassengers.AsNoTracking()
            .Where(x => x.ShuttleTripId == shuttleTripId)
            .ToArrayAsync(cancellationToken);

        string? scope = null;
        var allowed = false;
        if (string.Equals(role, "DRIVER", StringComparison.OrdinalIgnoreCase)
            && shuttleTrip.DriverUserId == userId)
        {
            allowed = true;
            scope = "DRIVER";
        }
        else if ((string.Equals(role, "OPERATOR_ADMIN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(role, "OPERATOR_STAFF", StringComparison.OrdinalIgnoreCase))
            && operatorId == shuttleTrip.OperatorId)
        {
            allowed = true;
            scope = "OPERATOR";
        }
        else if (string.Equals(role, "PASSENGER", StringComparison.OrdinalIgnoreCase)
            && manifests.Any(x => x.PassengerUserId == userId
                && x.Status is ShuttlePassenger.PendingStatus or ShuttlePassenger.PickedUpStatus))
        {
            allowed = true;
            scope = "PASSENGER";
        }

        var passengerStops = (scope == "PASSENGER"
                ? manifests.Where(x => x.PassengerUserId == userId)
                : manifests)
            .Where(x => x.PickupOrder.HasValue)
            .GroupBy(x => new { x.BookingId, x.PickupOrder })
            .Select(group => new
            {
                Manifest = group.First(),
                IsOwnPickup = group.Any(manifest => manifest.PassengerUserId == userId),
            })
            .OrderBy(x => x.Manifest.PickupOrder)
            .Select(x => new ShuttleTrackingStop(
                x.Manifest.PickupOrder!.Value,
                x.Manifest.BookingId,
                x.Manifest.PickupLat,
                x.Manifest.PickupLng,
                x.Manifest.Status,
                false,
                x.IsOwnPickup,
                x.Manifest.PickupAddress,
                x.Manifest.PickupOrder,
                x.Manifest.RoadDistanceMeters))
            .ToList();
        var station = await _db.Stations.AsNoTracking().SingleAsync(x => x.Id == shuttleTrip.StationId, cancellationToken);
        var stationPickupOrder = shuttleTrip.Direction == ShuttleTrip.OutboundDirection
            ? 1
            : passengerStops.Count == 0 ? 1 : passengerStops.Max(x => x.PickupOrder) + 1;
        var stops = shuttleTrip.Direction == ShuttleTrip.OutboundDirection
            ? new List<ShuttleTrackingStop>()
            : passengerStops;
        if (station.Latitude.HasValue && station.Longitude.HasValue)
        {
            stops.Add(new ShuttleTrackingStop(
                stationPickupOrder,
                null,
                station.Latitude.Value,
                station.Longitude.Value,
                shuttleTrip.Direction == ShuttleTrip.OutboundDirection
                    && shuttleTrip.Status != ShuttleTrip.ScheduledStatus
                    ? ShuttlePassenger.PickedUpStatus
                    : ShuttlePassenger.PendingStatus,
                true,
                false,
                station.Name,
                stationPickupOrder));
        }
        if (shuttleTrip.Direction == ShuttleTrip.OutboundDirection)
        {
            stops.AddRange(passengerStops.Select(stop => stop with
            {
                PickupOrder = stop.PickupOrder + 1,
                ServiceOrder = stop.ServiceOrder,
            }));
        }

        var trackingStation = new ShuttleTrackingStation(
            station.Id,
            station.Name,
            station.Latitude,
            station.Longitude,
            stationPickupOrder);

        return new ShuttleTrackingContext(
            shuttleTrip.Id,
            shuttleTrip.MainTripId,
            shuttleTrip.OperatorId,
            shuttleTrip.DriverUserId,
            allowed,
            scope,
            stops,
            trackingStation,
            shuttleTrip.Direction,
            shuttleTrip.Status);
    }

    public async Task<ShuttlePickupResult> MarkPickupAsync(
        Guid shuttleTripId,
        int pickupOrder,
        Guid driverUserId,
        CancellationToken cancellationToken)
    {
        if (pickupOrder <= 0)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Pickup order must be a positive integer.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await AcquireLockAsync("shuttle-pickup", shuttleTripId, cancellationToken);

        var shuttleTrip = await _db.ShuttleTrips
            .SingleOrDefaultAsync(x => x.Id == shuttleTripId, cancellationToken)
            ?? throw new CodedNotFoundException(
                "SHUTTLE_TRIP_NOT_FOUND",
                "Shuttle trip was not found.");
        if (shuttleTrip.DriverUserId != driverUserId)
        {
            throw new ForbiddenException(
                "FORBIDDEN",
                "Shuttle trip is not assigned to this driver.");
        }

        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(
            _identity,
            shuttleTrip.OperatorId,
            cancellationToken);
        await StopWriteEligibilityGuard.ValidateOperatorSubscriptionCanWriteAsync(
            _identity,
            shuttleTrip.OperatorId,
            requireShuttleModule: true,
            cancellationToken);

        if (shuttleTrip.Status is "COMPLETED" or "CANCELLED")
        {
            throw new CodedConflictException(
                "SHUTTLE_TRIP_TERMINAL",
                "A terminal Shuttle trip cannot accept pickup updates.");
        }
        if (shuttleTrip.Status != ShuttleTrip.InProgressStatus)
        {
            throw new CodedConflictException(
                "SHUTTLE_TRIP_INVALID_STATE",
                "Shuttle trip must be in progress before passenger pickup.");
        }

        var manifests = await _db.ShuttlePassengers
            .Where(x => x.ShuttleTripId == shuttleTripId && x.PickupOrder == pickupOrder)
            .ToArrayAsync(cancellationToken);
        if (manifests.Length == 0)
        {
            throw new CodedNotFoundException(
                "SHUTTLE_PICKUP_NOT_FOUND",
                "Shuttle pickup order was not found.");
        }

        var pickedUpAt = manifests
            .Where(x => x.PickedUpAt.HasValue)
            .Select(x => x.PickedUpAt!.Value)
            .DefaultIfEmpty(_clock.UtcNow)
            .Min();
        var changed = 0;
        foreach (var manifest in manifests.Where(x => x.Status == ShuttlePassenger.PendingStatus))
        {
            if (manifest.MarkPickedUp(pickedUpAt))
            {
                changed++;
            }
        }

        var activeCount = manifests.Count(x =>
            x.Status is ShuttlePassenger.PickedUpStatus or ShuttlePassenger.DeliveredStatus);
        if (activeCount == 0)
        {
            throw new CodedConflictException(
                "SHUTTLE_PICKUP_NOT_PENDING",
                "Shuttle pickup has no pending passengers.");
        }

        if (changed > 0)
        {
            foreach (var manifest in manifests.Where(x => x.Status == ShuttlePassenger.PickedUpStatus && x.PickedUpAt == pickedUpAt))
            {
                await _outbox.EnqueueAsync(
                    "trip.shuttle.picked_up",
                    JsonSerializer.Serialize(new
                    {
                        eventId = Guid.NewGuid(),
                        occurredAt = pickedUpAt,
                        shuttleTripId,
                        mainTripId = shuttleTrip.MainTripId,
                        operatorId = shuttleTrip.OperatorId,
                        bookingId = manifest.BookingId,
                        passengerUserId = manifest.PassengerUserId,
                        direction = shuttleTrip.Direction,
                        serviceAddress = manifest.PickupAddress,
                        serviceOrder = pickupOrder,
                        status = ShuttlePassenger.PickedUpStatus,
                        roadDistanceMeters = manifest.RoadDistanceMeters,
                    }),
                    cancellationToken);
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ShuttlePickupResult(
            shuttleTripId,
            pickupOrder,
            changed,
            pickedUpAt);
    }

    public Task<ShuttleLifecycleResult> MarkDeliveredAsync(
        Guid shuttleTripId,
        int pickupOrder,
        Guid driverUserId,
        CancellationToken cancellationToken)
        => MutatePassengerGroupAsync(
            shuttleTripId,
            pickupOrder,
            driverUserId,
            cancellationToken,
            (passenger, now) => passenger.MarkDelivered(now),
            "DELIVERED");

    public async Task<ShuttleLifecycleResult> MarkNoShowAsync(
        Guid shuttleTripId,
        int pickupOrder,
        Guid driverUserId,
        string reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new CodedValidationException("VALIDATION_ERROR", "A no-show reason is required.");
        }

        return await MutatePassengerGroupAsync(
            shuttleTripId,
            pickupOrder,
            driverUserId,
            cancellationToken,
            (passenger, _) => passenger.MarkNoShow(reason),
            "NO_SHOW");
    }

    public async Task<ShuttleLifecycleResult> StartAsync(
        Guid shuttleTripId,
        Guid driverUserId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await AcquireLockAsync("shuttle-lifecycle", shuttleTripId, cancellationToken);
        var shuttleTrip = await GetAssignedShuttleTripAsync(shuttleTripId, driverUserId, cancellationToken);
        await ValidateShuttleMutationEligibilityAsync(shuttleTrip.OperatorId, cancellationToken);
        var now = _clock.UtcNow;
        try
        {
            shuttleTrip.Start(now);
        }
        catch (InvalidOperationException exception)
        {
            throw new CodedConflictException("SHUTTLE_TRIP_INVALID_STATE", exception.Message);
        }
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ShuttleLifecycleResult(shuttleTripId, shuttleTrip.Status, 0, now);
    }

    public async Task<ShuttleLifecycleResult> CompleteAsync(
        Guid shuttleTripId,
        Guid driverUserId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await AcquireLockAsync("shuttle-lifecycle", shuttleTripId, cancellationToken);
        var shuttleTrip = await GetAssignedShuttleTripAsync(shuttleTripId, driverUserId, cancellationToken);
        await ValidateShuttleMutationEligibilityAsync(shuttleTrip.OperatorId, cancellationToken);
        var activePassengers = await _db.ShuttlePassengers.AnyAsync(
            passenger => passenger.ShuttleTripId == shuttleTripId
                && (passenger.Status == ShuttlePassenger.PendingStatus
                    || passenger.Status == ShuttlePassenger.PickedUpStatus),
            cancellationToken);
        if (activePassengers)
        {
            throw new CodedConflictException(
                "SHUTTLE_PASSENGERS_INCOMPLETE",
                "All Shuttle passengers must be delivered, no-show, or cancelled first.");
        }

        var now = _clock.UtcNow;
        bool transitioned;
        try
        {
            transitioned = shuttleTrip.Complete(now);
        }
        catch (InvalidOperationException exception)
        {
            throw new CodedConflictException("SHUTTLE_TRIP_INVALID_STATE", exception.Message);
        }

        if (transitioned)
        {
            foreach (var manifest in await _db.ShuttlePassengers
                .Where(x => x.ShuttleTripId == shuttleTripId)
                .ToArrayAsync(cancellationToken))
            {
                await _outbox.EnqueueAsync(
                    "trip.shuttle.completed",
                    JsonSerializer.Serialize(new
                    {
                        eventId = Guid.NewGuid(),
                        occurredAt = now,
                        shuttleTripId,
                        mainTripId = shuttleTrip.MainTripId,
                        operatorId = shuttleTrip.OperatorId,
                        bookingId = manifest.BookingId,
                        passengerUserId = manifest.PassengerUserId,
                        direction = shuttleTrip.Direction,
                        serviceAddress = manifest.PickupAddress,
                        serviceOrder = manifest.PickupOrder,
                        status = shuttleTrip.Status,
                        roadDistanceMeters = manifest.RoadDistanceMeters,
                    }),
                    cancellationToken);
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ShuttleLifecycleResult(shuttleTripId, shuttleTrip.Status, 0, now);
    }

    private async Task<ShuttleLifecycleResult> MutatePassengerGroupAsync(
        Guid shuttleTripId,
        int pickupOrder,
        Guid driverUserId,
        CancellationToken cancellationToken,
        Func<ShuttlePassenger, DateTimeOffset, bool> mutate,
        string status)
    {
        if (pickupOrder <= 0)
        {
            throw new CodedValidationException("VALIDATION_ERROR", "Service order must be a positive integer.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await AcquireLockAsync("shuttle-passenger", shuttleTripId, cancellationToken);
        var shuttleTrip = await GetAssignedShuttleTripAsync(shuttleTripId, driverUserId, cancellationToken);
        await ValidateShuttleMutationEligibilityAsync(shuttleTrip.OperatorId, cancellationToken);
        if (shuttleTrip.Status is ShuttleTrip.CompletedStatus or ShuttleTrip.CancelledStatus)
        {
            throw new CodedConflictException("SHUTTLE_TRIP_TERMINAL", "A terminal Shuttle trip cannot accept passenger updates.");
        }
        if (shuttleTrip.Status != ShuttleTrip.InProgressStatus)
        {
            throw new CodedConflictException(
                "SHUTTLE_TRIP_INVALID_STATE",
                "Shuttle trip must be in progress before passenger updates.");
        }

        var manifests = await _db.ShuttlePassengers
            .Where(x => x.ShuttleTripId == shuttleTripId && x.PickupOrder == pickupOrder)
            .ToArrayAsync(cancellationToken);
        if (manifests.Length == 0)
        {
            throw new CodedNotFoundException("SHUTTLE_PASSENGER_NOT_FOUND", "Shuttle passenger order was not found.");
        }

        var now = _clock.UtcNow;
        var changed = 0;
        foreach (var manifest in manifests)
        {
            try
            {
                if (mutate(manifest, now))
                {
                    changed++;
                }
            }
            catch (InvalidOperationException exception)
            {
                throw new CodedConflictException("SHUTTLE_PASSENGER_INVALID_STATE", exception.Message);
            }
            catch (ArgumentException exception)
            {
                throw new CodedValidationException("VALIDATION_ERROR", exception.Message);
            }
        }

        if (changed > 0)
        {
            var routingKey = status == ShuttlePassenger.DeliveredStatus
                ? "trip.shuttle.delivered"
                : "trip.shuttle.no_show";
            foreach (var manifest in manifests)
            {
                await _outbox.EnqueueAsync(
                    routingKey,
                    JsonSerializer.Serialize(new
                    {
                        eventId = Guid.NewGuid(),
                        occurredAt = now,
                        shuttleTripId,
                        mainTripId = shuttleTrip.MainTripId,
                        operatorId = shuttleTrip.OperatorId,
                        bookingId = manifest.BookingId,
                        passengerUserId = manifest.PassengerUserId,
                        direction = shuttleTrip.Direction,
                        serviceAddress = manifest.PickupAddress,
                        serviceOrder = pickupOrder,
                        status,
                        reason = manifest.CancelReason,
                        roadDistanceMeters = manifest.RoadDistanceMeters,
                    }),
                    cancellationToken);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ShuttleLifecycleResult(shuttleTripId, status, changed, now);
    }

    private async Task<ShuttleTrip> GetAssignedShuttleTripAsync(
        Guid shuttleTripId,
        Guid driverUserId,
        CancellationToken cancellationToken)
    {
        var shuttleTrip = await _db.ShuttleTrips.SingleOrDefaultAsync(x => x.Id == shuttleTripId, cancellationToken)
            ?? throw new CodedNotFoundException("SHUTTLE_TRIP_NOT_FOUND", "Shuttle trip was not found.");
        if (shuttleTrip.DriverUserId != driverUserId)
        {
            throw new ForbiddenException("FORBIDDEN", "Shuttle trip is not assigned to this driver.");
        }

        return shuttleTrip;
    }

    public async Task<ShuttleLifecycleResult> CancelRequestAsync(
        Guid operatorId,
        Guid mainTripId,
        Guid bookingId,
        string direction,
        string reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new CodedValidationException("VALIDATION_ERROR", "A cancellation reason is required.");
        }

        await ValidateShuttleMutationEligibilityAsync(operatorId, cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await AcquireLockAsync("shuttle-request", bookingId, cancellationToken);
        var manifests = await _db.ShuttlePassengers
            .Where(x => x.MainTripId == mainTripId
                && x.BookingId == bookingId
                && x.Direction == direction
                && x.Status == ShuttlePassenger.PendingAssignmentStatus
                && x.ShuttleTripId == null)
            .ToArrayAsync(cancellationToken);
        var mainTripExists = await _db.Trips.AsNoTracking().AnyAsync(
            x => x.Id == mainTripId && x.OperatorId == operatorId,
            cancellationToken);
        if (!mainTripExists)
        {
            throw new CodedNotFoundException("TRIP_NOT_FOUND", "Main trip was not found.");
        }
        if (manifests.Length == 0)
        {
            throw new CodedConflictException("SHUTTLE_REQUEST_NOT_CANCELLABLE", "Only unassigned shuttle requests can be cancelled.");
        }

        var changed = 0;
        foreach (var manifest in manifests)
        {
            if (!manifest.Cancel(reason)) continue;
            changed++;
            await _outbox.EnqueueAsync(
                "trip.shuttle.cancelled",
                JsonSerializer.Serialize(new
                {
                    eventId = Guid.NewGuid(),
                    occurredAt = _clock.UtcNow,
                    shuttleTripId = manifest.ShuttleTripId,
                    mainTripId,
                    operatorId,
                    bookingId,
                    passengerUserId = manifest.PassengerUserId,
                    direction,
                    serviceAddress = manifest.PickupAddress,
                    serviceOrder = manifest.PickupOrder,
                    status = ShuttlePassenger.CancelledStatus,
                    reason = manifest.CancelReason,
                    roadDistanceMeters = manifest.RoadDistanceMeters,
                }),
                cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ShuttleLifecycleResult(Guid.Empty, ShuttlePassenger.CancelledStatus, changed, _clock.UtcNow);
    }

    public async Task<ShuttleLifecycleResult> CancelShuttleTripAsync(
        Guid operatorId,
        Guid shuttleTripId,
        string reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new CodedValidationException("VALIDATION_ERROR", "A cancellation reason is required.");
        }

        await ValidateShuttleMutationEligibilityAsync(operatorId, cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await AcquireLockAsync("shuttle-lifecycle", shuttleTripId, cancellationToken);
        var shuttleTrip = await _db.ShuttleTrips.SingleOrDefaultAsync(
            x => x.Id == shuttleTripId && x.OperatorId == operatorId,
            cancellationToken)
            ?? throw new CodedNotFoundException("SHUTTLE_TRIP_NOT_FOUND", "Shuttle trip was not found.");
        var now = _clock.UtcNow;
        bool transitioned;
        try
        {
            transitioned = shuttleTrip.Cancel(reason);
        }
        catch (InvalidOperationException exception)
        {
            throw new CodedConflictException("SHUTTLE_TRIP_INVALID_STATE", exception.Message);
        }

        if (!transitioned)
        {
            return new ShuttleLifecycleResult(shuttleTripId, shuttleTrip.Status, 0, null);
        }
        var manifests = await _db.ShuttlePassengers
            .Where(x => x.ShuttleTripId == shuttleTripId)
            .ToArrayAsync(cancellationToken);
        foreach (var manifest in manifests)
        {
            if (!manifest.Cancel(reason)) continue;
            await _outbox.EnqueueAsync(
                "trip.shuttle.cancelled",
                JsonSerializer.Serialize(new
                {
                    eventId = Guid.NewGuid(),
                    occurredAt = now,
                    shuttleTripId,
                    mainTripId = shuttleTrip.MainTripId,
                    operatorId,
                    bookingId = manifest.BookingId,
                    passengerUserId = manifest.PassengerUserId,
                    direction = shuttleTrip.Direction,
                    serviceAddress = manifest.PickupAddress,
                    serviceOrder = manifest.PickupOrder,
                    status = ShuttlePassenger.CancelledStatus,
                    reason,
                    roadDistanceMeters = manifest.RoadDistanceMeters,
                }),
                cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ShuttleLifecycleResult(shuttleTripId, shuttleTrip.Status, manifests.Length, now);
    }

    private async Task ValidateShuttleMutationEligibilityAsync(
        Guid operatorId,
        CancellationToken cancellationToken)
    {
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(
            _identity,
            operatorId,
            cancellationToken);
        await StopWriteEligibilityGuard.ValidateOperatorSubscriptionCanWriteAsync(
            _identity,
            operatorId,
            requireShuttleModule: true,
            cancellationToken);
    }

    private Task AcquireLockAsync(string resource, Guid id, CancellationToken cancellationToken)
        => _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({resource + ':' + id.ToString("N")}, 0))",
            cancellationToken);

    private async Task<bool> HasDriverConflictAsync(CreateShuttleTripInput input, CancellationToken cancellationToken)
        => await _db.ShuttleTrips.AnyAsync(x => x.DriverUserId == input.DriverUserId
                && x.Status != "COMPLETED" && x.Status != "CANCELLED"
                && x.ScheduledDepartureTime < input.ScheduledEndTime
                && input.ScheduledDepartureTime < x.ScheduledEndTime, cancellationToken)
            || await _db.Trips.AnyAsync(x => x.DriverUserId == input.DriverUserId
                && x.Status != TripStatus.COMPLETED && x.Status != TripStatus.CANCELLED
                && x.DepartureDateTime < input.ScheduledEndTime
                && input.ScheduledDepartureTime < x.EstimatedArrivalTime, cancellationToken);

    private async Task<bool> HasVehicleConflictAsync(CreateShuttleTripInput input, CancellationToken cancellationToken)
        => await _db.ShuttleTrips.AnyAsync(x => x.VehicleId == input.VehicleId
                && x.Status != "COMPLETED" && x.Status != "CANCELLED"
                && x.ScheduledDepartureTime < input.ScheduledEndTime
                && input.ScheduledDepartureTime < x.ScheduledEndTime, cancellationToken)
            || await _db.Trips.AnyAsync(x => x.VehicleId == input.VehicleId
                && x.Status != TripStatus.COMPLETED && x.Status != TripStatus.CANCELLED
                && x.DepartureDateTime < input.ScheduledEndTime
                && input.ScheduledDepartureTime < x.EstimatedArrivalTime, cancellationToken);

}
