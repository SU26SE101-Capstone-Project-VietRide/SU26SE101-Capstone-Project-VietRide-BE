using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.Time;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.Internal.Trips.Tracking;
using VietRide.Trip.Application.Features.ResourceAvailability;
using VietRide.Trip.Application.Features.Stops;
using VietRide.Trip.Application.Features.Trips.Operations;
using VietRide.Trip.Application.Features.Vehicles;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Services;

internal sealed class ShuttleDispatchService : IShuttleDispatchService
{
    private const int ArrivalBufferMinutes = 30;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TripDbContext _db;
    private readonly IIdentityInternalClient _identity;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;
    private readonly IResourceAvailabilityService _resourceAvailability;
    private readonly int _maxShuttleDistanceMeters;

    public ShuttleDispatchService(
        TripDbContext db,
        IIdentityInternalClient identity,
        IIntegrationEventOutbox outbox,
        IClock clock,
        IResourceAvailabilityService resourceAvailability,
        IConfiguration configuration)
    {
        _db = db;
        _identity = identity;
        _outbox = outbox;
        _clock = clock;
        _resourceAvailability = resourceAvailability;
        var maxDistanceKm = configuration.GetValue<int?>("SHUTTLE_MAX_DISTANCE_KM")
            ?? ShuttleDistancePolicy.DefaultMaxDistanceKm;
        _maxShuttleDistanceMeters = checked(Math.Max(1, maxDistanceKm) * 1_000);
    }

    public async Task<ShuttleRequestPage> GetPendingAsync(
        Guid operatorId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
        => await GetPendingFilteredAsync(
            operatorId, page, pageSize, null, null, null, null, [], cancellationToken);

    public async Task<ShuttleRequestPage> GetPendingFilteredAsync(
        Guid operatorId,
        int page,
        int pageSize,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtcExclusive,
        Guid? mainTripId,
        string? search,
        IReadOnlyCollection<Guid> passengerUserIds,
        CancellationToken cancellationToken)
    {
        var passengerQuery = _db.ShuttlePassengers.AsNoTracking()
            .Where(passenger => passenger.Status == ShuttlePassenger.PendingAssignmentStatus);
        if (mainTripId.HasValue) passengerQuery = passengerQuery.Where(passenger => passenger.MainTripId == mainTripId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            passengerQuery = passengerQuery.Where(passenger => EF.Functions.ILike(passenger.PickupAddress, pattern, "\\")
                || (passenger.PassengerUserId.HasValue && passengerUserIds.Contains(passenger.PassengerUserId.Value)));
        }

        var tripQuery = _db.Trips.AsNoTracking().Where(trip => trip.OperatorId == operatorId);
        if (fromUtc.HasValue) tripQuery = tripQuery.Where(trip => trip.DepartureDateTime >= fromUtc.Value);
        if (toUtcExclusive.HasValue) tripQuery = tripQuery.Where(trip => trip.DepartureDateTime < toUtcExclusive.Value);

        var joinedPendingQuery = passengerQuery.Join(
                tripQuery,
                passenger => passenger.MainTripId,
                trip => trip.Id,
                (passenger, trip) => new
                {
                    Trip = trip,
                    passenger.Direction,
                });
        var directionQuery = joinedPendingQuery
            .Select(item => new
            {
                Id = item.Trip.Id,
                item.Direction,
                item.Trip.DepartureDateTime,
                item.Trip.EstimatedArrivalTime,
            })
            .Distinct();

        var totalItems = await directionQuery.CountAsync(cancellationToken);
        var totalPendingPassengerCount = await joinedPendingQuery.LongCountAsync(cancellationToken);
        var pagedDirections = await directionQuery
            .OrderBy(item => item.Direction == ShuttleTrip.InboundDirection
                ? item.DepartureDateTime.AddMinutes(-ArrivalBufferMinutes)
                : item.EstimatedArrivalTime.AddMinutes(ArrivalBufferMinutes))
            .ThenBy(item => item.DepartureDateTime)
            .ThenBy(item => item.Id)
            .ThenBy(item => item.Direction)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        if (pagedDirections.Length == 0)
        {
            return ShuttleRequestPage.Create([], page, pageSize, totalItems, totalPendingPassengerCount);
        }

        var tripIds = pagedDirections.Select(item => item.Id).Distinct().ToArray();
        var trips = await _db.Trips.AsNoTracking()
            .Where(trip => tripIds.Contains(trip.Id))
            .ToDictionaryAsync(trip => trip.Id, cancellationToken);
        var routeIds = trips.Values.Select(trip => trip.RouteId).Distinct().ToArray();
        var routes = await _db.Routes.AsNoTracking()
            .Where(route => routeIds.Contains(route.Id))
            .ToDictionaryAsync(route => route.Id, cancellationToken);
        var stationIds = pagedDirections
            .Select(item =>
            {
                var route = routes[trips[item.Id].RouteId];
                return item.Direction == ShuttleTrip.InboundDirection
                    ? route.OriginStationId
                    : route.DestinationStationId;
            })
            .Distinct()
            .ToArray();
        var stations = await _db.Stations.AsNoTracking()
            .Where(station => stationIds.Contains(station.Id))
            .ToDictionaryAsync(station => station.Id, cancellationToken);
        var manifests = await _db.ShuttlePassengers.AsNoTracking()
            .Where(passenger => tripIds.Contains(passenger.MainTripId))
            .ToArrayAsync(cancellationToken);
        var pendingManifests = await passengerQuery
            .Where(passenger => tripIds.Contains(passenger.MainTripId))
            .ToArrayAsync(cancellationToken);
        var passengerProfiles = await GetIdentityProfilesOrThrowAsync(
            pendingManifests.Select(manifest => manifest.PassengerUserId),
            cancellationToken);
        var manifestsByDirection = pendingManifests
            .GroupBy(manifest => (manifest.MainTripId, manifest.Direction))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var allManifestsByDirection = manifests
            .GroupBy(manifest => (manifest.MainTripId, manifest.Direction))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var dispatchedTripsByDirection = await _db.ShuttleTrips.AsNoTracking()
            .Where(shuttle => tripIds.Contains(shuttle.MainTripId)
                && shuttle.Status != ShuttleTrip.CancelledStatus)
            .GroupBy(shuttle => new { shuttle.MainTripId, shuttle.Direction })
            .Select(group => new
            {
                group.Key.MainTripId,
                group.Key.Direction,
                Count = group.Count(),
            })
            .ToDictionaryAsync(
                item => (item.MainTripId, item.Direction),
                item => item.Count,
                cancellationToken);

        var items = pagedDirections.Select(item =>
        {
            var trip = trips[item.Id];
            var route = routes[trip.RouteId];
            var stationId = item.Direction == ShuttleTrip.InboundDirection
                ? route.OriginStationId
                : route.DestinationStationId;
            var station = stations[stationId];
            var groupedManifests = manifestsByDirection.GetValueOrDefault((item.Id, item.Direction)) ?? [];
            var groups = groupedManifests
                .Where(manifest => manifest.BookingId.HasValue)
                .GroupBy(manifest => manifest.BookingId!.Value)
                .Select(group =>
                {
                    var first = group.OrderBy(manifest => manifest.CreatedAt).First();
                    var passengers = group
                        .GroupBy(manifest => manifest.PassengerUserId)
                        .OrderBy(passenger => passenger.Key)
                        .Select(passenger => new ShuttlePassengerProfile(
                            passenger.Key,
                            passenger.Key.HasValue && passengerProfiles.TryGetValue(passenger.Key.Value, out var profile)
                                ? profile.DisplayName
                                : null,
                            passenger.Key.HasValue && passengerProfiles.TryGetValue(passenger.Key.Value, out profile)
                                ? profile.Phone
                                : null,
                            passenger
                                .Where(manifest => manifest.TicketId.HasValue)
                                .Select(manifest => manifest.TicketId!.Value)
                                .Distinct()
                                .ToArray()))
                        .ToArray();
                    return new ShuttleBookingGroup(
                        group.Key,
                        first.BookingCode,
                        group.Count(),
                        first.PickupAddress,
                        first.PickupLat,
                        first.PickupLng,
                        first.RoadDistanceMeters,
                        first.CreatedAt,
                        first.RoadDistanceMeters,
                        passengers);
                })
                .OrderBy(group => group.RequestedAt)
                .ToArray();
            var suggested = groups
                .OrderByDescending(group => group.DistanceToStationMeters)
                .ThenBy(group => group.RequestedAt)
                .Select(group => group.BookingId)
                .ToArray();
            var allDirectionManifests = allManifestsByDirection.GetValueOrDefault((item.Id, item.Direction)) ?? [];
            var assignedPassengerCount = allDirectionManifests.Count(manifest =>
                manifest.ShuttleTripId.HasValue
                && manifest.Status is ShuttlePassenger.PendingStatus
                    or ShuttlePassenger.PickedUpStatus
                    or ShuttlePassenger.DeliveredStatus
                    or ShuttlePassenger.NoShowStatus);
            return new ShuttleRequestTripGroup(
                trip.Id,
                route.Name,
                item.Direction,
                trip.DepartureDateTime,
                item.Direction == ShuttleTrip.InboundDirection
                    ? trip.DepartureDateTime.AddMinutes(-ArrivalBufferMinutes)
                    : trip.EstimatedArrivalTime.AddMinutes(ArrivalBufferMinutes),
                station.Id,
                station.Name,
                groupedManifests.Length,
                assignedPassengerCount,
                groupedManifests.Length + assignedPassengerCount,
                dispatchedTripsByDirection.GetValueOrDefault((item.Id, item.Direction)),
                groups,
                suggested);
        }).ToArray();

        return ShuttleRequestPage.Create(items, page, pageSize, totalItems, totalPendingPassengerCount);
    }

    public async Task<PagedResult<OperatorShuttleTripListItemDto>> GetHistoryAsync(
        Guid operatorId,
        int page,
        int pageSize,
        DateOnly? from,
        DateOnly? to,
        IReadOnlyCollection<string>? statuses,
        CancellationToken cancellationToken)
    {
        var query = _db.ShuttleTrips.AsNoTracking()
            .Where(shuttle => shuttle.OperatorId == operatorId);
        var fromUtc = from.HasValue ? ToUtcBoundary(from.Value) : (DateTimeOffset?)null;
        var toUtc = to.HasValue ? ToUtcBoundary(to.Value.AddDays(1)) : (DateTimeOffset?)null;
        if (fromUtc.HasValue)
        {
            query = query.Where(shuttle => shuttle.ScheduledDepartureTime >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(shuttle => shuttle.ScheduledDepartureTime < toUtc.Value);
        }

        var normalizedStatuses = statuses?
            .Select(status => status.Trim().ToUpperInvariant())
            .Where(status => status.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedStatuses is { Length: > 0 })
        {
            query = query.Where(shuttle => normalizedStatuses.Contains(shuttle.Status));
        }

        var totalItems = await query.CountAsync(cancellationToken);
        var rows = await (
            from shuttle in query
            join vehicle in _db.Vehicles.AsNoTracking() on shuttle.VehicleId equals vehicle.Id
            orderby shuttle.ScheduledDepartureTime descending, shuttle.Id descending
            select new
            {
                shuttle.Id,
                shuttle.MainTripId,
                shuttle.Direction,
                shuttle.Status,
                shuttle.ScheduledDepartureTime,
                shuttle.ScheduledEndTime,
                shuttle.ActualDepartureTime,
                shuttle.CompletedAt,
                VehicleId = vehicle.Id,
                vehicle.LicensePlate,
                shuttle.DriverUserId,
                PassengerCount = _db.ShuttlePassengers.Count(passenger =>
                    passenger.ShuttleTripId == shuttle.Id
                    && passenger.Status != ShuttlePassenger.CancelledStatus),
                StopCount = _db.ShuttlePassengers
                    .Where(passenger => passenger.ShuttleTripId == shuttle.Id
                        && passenger.PickupOrder.HasValue
                        && passenger.Status != ShuttlePassenger.CancelledStatus)
                    .Select(passenger => passenger.PickupOrder)
                    .Distinct()
                    .Count(),
            })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        var driverIds = rows.Select(row => row.DriverUserId).Distinct().ToArray();
        var profiles = await GetIdentityProfilesOrThrowAsync(driverIds.Select(id => (Guid?)id), cancellationToken);
        var items = rows.Select(row => new OperatorShuttleTripListItemDto(
            row.Id,
            row.MainTripId,
            row.Direction,
            row.Status,
            row.ScheduledDepartureTime,
            row.ScheduledEndTime,
            row.ActualDepartureTime,
            row.CompletedAt,
            new OperatorShuttleVehicleDto(row.VehicleId, row.LicensePlate),
            profiles.TryGetValue(row.DriverUserId, out var profile)
                ? new OperatorShuttleDriverDto(row.DriverUserId, profile.DisplayName, profile.Phone)
                : new OperatorShuttleDriverDto(row.DriverUserId, null, null),
            row.PassengerCount,
            row.StopCount)).ToArray();

        return PagedResult<OperatorShuttleTripListItemDto>.Create(items, page, pageSize, totalItems);
    }

    public async Task<IReadOnlyList<OperatorTrackingShuttleTripDto>> GetTrackingProjectionAsync(
        Guid operatorId,
        CancellationToken cancellationToken)
        => await _db.ShuttleTrips.AsNoTracking()
            .Where(shuttle => shuttle.OperatorId == operatorId
                && shuttle.Status == ShuttleTrip.InProgressStatus)
            .OrderBy(shuttle => shuttle.Id)
            .Select(shuttle => new OperatorTrackingShuttleTripDto(
                shuttle.Id,
                shuttle.MainTripId,
                shuttle.Status))
            .ToArrayAsync(cancellationToken);

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

        var availabilityInput = new ShuttleAvailabilityInput(
            input.OperatorId,
            input.MainTripId,
            input.Direction,
            input.DriverUserId,
            input.VehicleId,
            input.ScheduledDepartureTime,
            input.ScheduledEndTime,
            input.OrderedBookingIds);
        ResourceAvailabilityConflictGuard.EnsureAvailable(
            await _resourceAvailability.CheckShuttleAsync(
                availabilityInput,
                acquireLocks: false,
                cancellationToken),
            AssignmentSourceType.SHUTTLE_TRIP);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        ResourceAvailabilityConflictGuard.EnsureAvailable(
            await _resourceAvailability.CheckShuttleAsync(
                availabilityInput,
                acquireLocks: true,
                cancellationToken),
            AssignmentSourceType.SHUTTLE_TRIP);

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

        var vehicleLayout = vehicle.SeatLayoutJson.Deserialize<SeatLayoutDto>(JsonOptions)
            ?? throw new CodedValidationException("VALIDATION_ERROR", "Vehicle seat layout is invalid.");
        var usablePassengerCapacity = SeatLayoutMetrics.CountUsablePassengerSeats(vehicleLayout);
        if (manifests.Length > usablePassengerCapacity)
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

        await _resourceAvailability.ReserveShuttleTripAsync(
            shuttleTrip,
            input.OrderedBookingIds,
            cancellationToken);

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

    private (DateOnly From, DateOnly To) ResolveDriverDateRange(DateOnly? from, DateOnly? to)
    {
        var today = BusinessTime.ToLocalDate(_clock.UtcNow);
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
        BusinessTime.ToUtc(date, TimeOnly.MinValue);

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
            await _resourceAvailability.ActivateShuttleTripAsync(shuttleTripId, now, cancellationToken);
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
            await _resourceAvailability.ReleaseShuttleTripAsync(shuttleTripId, now, cancellationToken);
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
        await _resourceAvailability.CancelShuttleTripAsync(shuttleTripId, now, cancellationToken);
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

    private async Task<IReadOnlyDictionary<Guid, IdentityUserProfile>> GetIdentityProfilesOrThrowAsync(
        IEnumerable<Guid?> userIds,
        CancellationToken cancellationToken)
    {
        var distinctIds = userIds
            .Where(id => id.HasValue && id.Value != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        if (distinctIds.Length == 0)
        {
            return new Dictionary<Guid, IdentityUserProfile>();
        }

        try
        {
            return await _identity.GetUsersAsync(distinctIds, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TripIdentityUnavailableException(
                "Identity profile lookup failed due to an upstream transport error.",
                exception);
        }
    }

    private Task AcquireLockAsync(string resource, Guid id, CancellationToken cancellationToken)
        => _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({resource + ':' + id.ToString("N")}, 0))",
            cancellationToken);

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

}
