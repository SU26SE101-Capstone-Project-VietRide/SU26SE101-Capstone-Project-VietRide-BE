using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Services;

internal sealed class ShuttleDispatchService : IShuttleDispatchService
{
    private const int ArrivalBufferMinutes = 30;
    private readonly TripDbContext _db;
    private readonly IIdentityInternalClient _identity;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public ShuttleDispatchService(
        TripDbContext db,
        IIdentityInternalClient identity,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _db = db;
        _identity = identity;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<ShuttleRequestPage> GetPendingAsync(
        Guid operatorId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var tripIds = await _db.ShuttlePassengers.AsNoTracking()
            .Where(passenger => passenger.Status == ShuttlePassenger.PendingAssignmentStatus)
            .Join(_db.Trips.AsNoTracking().Where(trip => trip.OperatorId == operatorId),
                passenger => passenger.MainTripId,
                trip => trip.Id,
                (passenger, trip) => trip.Id)
            .Distinct()
            .OrderBy(id => id)
            .ToArrayAsync(cancellationToken);

        var pagedIds = tripIds.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        var items = new List<ShuttleRequestTripGroup>();
        foreach (var tripId in pagedIds)
        {
            var trip = await _db.Trips.AsNoTracking().SingleAsync(x => x.Id == tripId, cancellationToken);
            var route = await _db.Routes.AsNoTracking().SingleAsync(x => x.Id == trip.RouteId, cancellationToken);
            var station = await _db.Stations.AsNoTracking().SingleAsync(x => x.Id == route.OriginStationId, cancellationToken);
            var manifests = await _db.ShuttlePassengers.AsNoTracking()
                .Where(x => x.MainTripId == tripId && x.Status == ShuttlePassenger.PendingAssignmentStatus)
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
                        DistanceMeters(first.PickupLat, first.PickupLng, station.Latitude!.Value, station.Longitude!.Value),
                        first.CreatedAt);
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
                trip.DepartureDateTime,
                trip.DepartureDateTime.AddMinutes(-ArrivalBufferMinutes),
                station.Id,
                station.Name,
                manifests.Length,
                groups,
                suggested));
        }

        return new ShuttleRequestPage(items, page, pageSize, tripIds.Length);
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

        var hardCutoffAt = trip.DepartureDateTime.AddMinutes(-ArrivalBufferMinutes);
        if (_clock.UtcNow >= hardCutoffAt)
        {
            throw new ConflictException(
                "SHUTTLE_REQUEST_CUTOFF_PASSED",
                "The shuttle dispatch cutoff has passed.");
        }

        if (input.ScheduledEndTime <= input.ScheduledDepartureTime
            || input.ScheduledEndTime > hardCutoffAt)
        {
            throw new CodedValidationException("VALIDATION_ERROR", "Shuttle schedule violates the main-trip arrival buffer.");
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
            .FromSqlInterpolated($"SELECT * FROM vietride_trip.shuttle_passengers WHERE main_trip_id = {input.MainTripId} AND booking_id = ANY({selectedIds}) FOR UPDATE")
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
        if (_clock.UtcNow >= hardCutoffAt)
        {
            throw new ConflictException(
                "SHUTTLE_REQUEST_CUTOFF_PASSED",
                "The shuttle dispatch cutoff has passed.");
        }

        if (manifests.Length > vehicle.TotalSeats)
        {
            throw new ConflictException("SHUTTLE_CAPACITY_EXCEEDED", "Selected passengers exceed vehicle capacity.");
        }

        var route = await _db.Routes.AsNoTracking().SingleAsync(x => x.Id == trip.RouteId, cancellationToken);
        var shuttleTrip = ShuttleTrip.Create(
            input.OperatorId,
            input.MainTripId,
            route.OriginStationId,
            input.DriverUserId,
            input.VehicleId,
            input.ScheduledDepartureTime,
            input.ScheduledEndTime,
            input.Notes);
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
                shuttleTripId = shuttleTrip.Id,
                mainTripId = input.MainTripId,
                bookingId,
                passengerUserId,
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
            x => x.MainTripId == input.MainTripId && x.Status == ShuttlePassenger.PendingAssignmentStatus,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CreateShuttleTripResult(shuttleTrip.Id, input.MainTripId, manifests.Length, remaining);
    }

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
            && manifests.Any(x => x.PassengerUserId == userId && x.Status == ShuttlePassenger.PendingStatus))
        {
            allowed = true;
            scope = "PASSENGER";
        }

        var stops = manifests
            .Where(x => x.PickupOrder.HasValue)
            .GroupBy(x => new { x.BookingId, x.PickupOrder })
            .Select(group => group.First())
            .OrderBy(x => x.PickupOrder)
            .Select(x => new ShuttleTrackingStop(
                x.PickupOrder!.Value,
                x.BookingId,
                x.PickupLat,
                x.PickupLng,
                x.Status,
                false))
            .ToList();
        var station = await _db.Stations.AsNoTracking().SingleAsync(x => x.Id == shuttleTrip.StationId, cancellationToken);
        if (station.Latitude.HasValue && station.Longitude.HasValue)
        {
            stops.Add(new ShuttleTrackingStop(
                stops.Count == 0 ? 1 : stops.Max(x => x.PickupOrder) + 1,
                null,
                station.Latitude.Value,
                station.Longitude.Value,
                "PENDING",
                true));
        }

        return new ShuttleTrackingContext(
            shuttleTrip.Id,
            shuttleTrip.MainTripId,
            shuttleTrip.OperatorId,
            shuttleTrip.DriverUserId,
            allowed,
            scope,
            stops);
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

        if (shuttleTrip.Status is "COMPLETED" or "CANCELLED")
        {
            throw new CodedConflictException(
                "SHUTTLE_TRIP_TERMINAL",
                "A terminal Shuttle trip cannot accept pickup updates.");
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

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ShuttlePickupResult(
            shuttleTripId,
            pickupOrder,
            changed,
            pickedUpAt);
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

    private static int DistanceMeters(decimal lat1, decimal lng1, decimal lat2, decimal lng2)
    {
        const double earthRadiusMeters = 6_371_000d;
        var latitudeDelta = DegreesToRadians((double)(lat2 - lat1));
        var longitudeDelta = DegreesToRadians((double)(lng2 - lng1));
        var a = Math.Sin(latitudeDelta / 2) * Math.Sin(latitudeDelta / 2)
            + Math.Cos(DegreesToRadians((double)lat1)) * Math.Cos(DegreesToRadians((double)lat2))
            * Math.Sin(longitudeDelta / 2) * Math.Sin(longitudeDelta / 2);
        return (int)Math.Round(earthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
}
