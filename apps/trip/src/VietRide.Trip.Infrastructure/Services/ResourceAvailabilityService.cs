using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Time;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.ResourceAvailability;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Services;

internal sealed class ResourceAvailabilityService : IResourceAvailabilityService
{
    private const int TurnaroundMinutes = ResourceAvailabilityPolicy.TurnaroundMinutes;
    private const int MaxConflicts = 100;
    private const int RecurrenceComparisonDays = 15;

    private readonly TripDbContext db;
    private readonly IRepositionTravelTimeClient travelTimeClient;
    private readonly IClock clock;
    private readonly Dictionary<LocationPair, int> travelTimeCache = [];

    public ResourceAvailabilityService(
        TripDbContext db,
        IRepositionTravelTimeClient travelTimeClient,
        IClock clock)
    {
        this.db = db;
        this.travelTimeClient = travelTimeClient;
        this.clock = clock;
    }

    public async Task<ResourceAvailabilityResult> CheckDriverScheduleAsync(
        DriverScheduleAvailabilityInput input,
        bool acquireLocks,
        CancellationToken cancellationToken = default)
    {
        ValidateScheduleInput(input);
        var candidateRoute = await LoadRoutePlanAsync(input.RouteId, input.OperatorId, cancellationToken);
        EnsureLocationCoordinates(candidateRoute.StartLocation);
        EnsureLocationCoordinates(candidateRoute.EndLocation);
        var candidateResources = BuildResources(input.DriverUserId, input.AssistantUserId, input.VehicleId);
        if (acquireLocks)
        {
            await AcquireResourceLocksAsync(candidateResources, cancellationToken);
        }

        var existingSchedules = await db.DriverSchedules
            .AsNoTracking()
            .Where(schedule => schedule.OperatorId == input.OperatorId
                && schedule.IsActive
                && schedule.DeletedAt == null
                && (!input.ExcludeScheduleId.HasValue || schedule.Id != input.ExcludeScheduleId.Value)
                && (!schedule.ValidUntil.HasValue || schedule.ValidUntil.Value >= input.ValidFrom)
                && (!input.ValidUntil.HasValue || schedule.ValidFrom <= input.ValidUntil.Value)
                && (schedule.DriverUserId == input.DriverUserId
                    || (input.AssistantUserId.HasValue
                        && (schedule.DriverUserId == input.AssistantUserId.Value
                            || schedule.AssistantUserId == input.AssistantUserId.Value))
                    || (schedule.AssistantUserId.HasValue
                        && schedule.AssistantUserId.Value == input.DriverUserId)
                    || (input.VehicleId.HasValue && schedule.VehicleId == input.VehicleId.Value)))
            .OrderBy(schedule => schedule.Id)
            .ToArrayAsync(cancellationToken);

        var conflicts = new List<ResourceAvailabilityConflict>();
        foreach (var schedule in existingSchedules)
        {
            var overlapStart = Max(input.ValidFrom, schedule.ValidFrom);
            var overlapEnd = Min(input.ValidUntil, schedule.ValidUntil);
            if (overlapEnd.HasValue && overlapEnd.Value < overlapStart)
            {
                continue;
            }

            var existingRoute = await LoadRoutePlanAsync(schedule.RouteId, schedule.OperatorId, cancellationToken);
            var comparisonEnd = overlapStart.AddDays(RecurrenceComparisonDays - 1);
            if (overlapEnd.HasValue && overlapEnd.Value < comparisonEnd)
            {
                comparisonEnd = overlapEnd.Value;
            }

            var candidateOccurrences = BuildOccurrences(
                input.ValidFrom,
                input.ValidUntil,
                input.DayOfWeek,
                input.DepartureTime,
                candidateRoute,
                overlapStart.AddDays(-7),
                comparisonEnd);
            var existingOccurrences = BuildOccurrences(
                schedule.ValidFrom,
                schedule.ValidUntil,
                ParseDays(schedule.DayOfWeek),
                schedule.DepartureTime,
                existingRoute,
                overlapStart.AddDays(-7),
                comparisonEnd);
            var existingResources = BuildResources(
                schedule.DriverUserId,
                schedule.AssistantUserId,
                schedule.VehicleId);

            foreach (var candidateOccurrence in candidateOccurrences)
            {
                foreach (var existingOccurrence in existingOccurrences)
                {
                    foreach (var candidateResource in candidateResources)
                    {
                        var matching = existingResources.FirstOrDefault(existing =>
                            existing.ResourceType == candidateResource.ResourceType
                            && existing.ResourceId == candidateResource.ResourceId);
                        if (matching is null)
                        {
                            continue;
                        }

                        var conflict = await CompareAsync(
                            candidateOccurrence,
                            existingOccurrence,
                            candidateResource,
                            AssignmentSourceType.DRIVER_SCHEDULE,
                            schedule.Id,
                            cancellationToken);
                        AddConflict(conflicts, conflict);
                        if (conflicts.Count > MaxConflicts)
                        {
                            return ToResult(conflicts);
                        }
                    }
                }
            }
        }

        var representativeEnd = input.ValidFrom.AddDays(RecurrenceComparisonDays - 1);
        if (input.ValidUntil.HasValue && input.ValidUntil.Value < representativeEnd)
        {
            representativeEnd = input.ValidUntil.Value;
        }

        var selfOccurrences = BuildOccurrences(
            input.ValidFrom,
            input.ValidUntil,
            input.DayOfWeek,
            input.DepartureTime,
            candidateRoute,
            input.ValidFrom,
            representativeEnd);
        for (var leftIndex = 0; leftIndex < selfOccurrences.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < selfOccurrences.Count; rightIndex++)
            {
                foreach (var resource in candidateResources)
                {
                    var conflict = await CompareAsync(
                        selfOccurrences[rightIndex],
                        selfOccurrences[leftIndex],
                        resource,
                        AssignmentSourceType.DRIVER_SCHEDULE,
                        input.ExcludeScheduleId ?? Guid.Empty,
                        cancellationToken);
                    AddConflict(conflicts, conflict);
                    if (conflicts.Count > MaxConflicts)
                    {
                        return ToResult(conflicts);
                    }
                }
            }
        }

        await CompareScheduleWithConcreteReservationsAsync(
            input,
            candidateRoute,
            candidateResources,
            conflicts,
            cancellationToken);
        return ToResult(conflicts);
    }

    public async Task<ResourceAvailabilityResult> CheckShuttleAsync(
        ShuttleAvailabilityInput input,
        bool acquireLocks,
        CancellationToken cancellationToken = default)
    {
        var candidate = await BuildShuttleCandidateAsync(input, cancellationToken);
        return await CheckCandidateAsync(candidate, acquireLocks, cancellationToken);
    }

    public async Task<ResourceAvailabilityResult> CheckCandidateAsync(
        ResourceAvailabilityCandidate candidate,
        bool acquireLocks,
        CancellationToken cancellationToken = default) =>
        await CheckCandidateAsync(candidate, acquireLocks, [], cancellationToken);

    private async Task<ResourceAvailabilityResult> CheckCandidateAsync(
        ResourceAvailabilityCandidate candidate,
        bool acquireLocks,
        IReadOnlyCollection<Guid> additionallyExcludedTripIds,
        CancellationToken cancellationToken)
    {
        ValidateCandidate(candidate);
        if (acquireLocks)
        {
            await AcquireResourceLocksAsync(candidate.Resources, cancellationToken);
        }

        var resourceIds = candidate.Resources.Select(resource => resource.ResourceId).Distinct().ToArray();
        var excludedTripIds = additionallyExcludedTripIds
            .Concat(candidate.ExcludedTripId.HasValue ? [candidate.ExcludedTripId.Value] : [])
            .Distinct()
            .ToArray();
        var existing = await db.ResourceReservations
            .Where(reservation => resourceIds.Contains(reservation.ResourceId)
                && (reservation.Status == ResourceReservationStatus.RESERVED
                    || reservation.Status == ResourceReservationStatus.ACTIVE)
                && (!reservation.TripId.HasValue || !excludedTripIds.Contains(reservation.TripId.Value))
                && (!candidate.ExcludedShuttleTripId.HasValue
                    || reservation.ShuttleTripId != candidate.ExcludedShuttleTripId.Value))
            .OrderBy(reservation => reservation.PlannedStartAt)
            .ThenBy(reservation => reservation.Id)
            .ToArrayAsync(cancellationToken);
        existing = existing
            .Where(reservation => reservation.Status is ResourceReservationStatus.RESERVED
                or ResourceReservationStatus.ACTIVE)
            .ToArray();

        var assignment = ToAssignment(candidate);
        var conflicts = new List<ResourceAvailabilityConflict>();
        foreach (var resource in candidate.Resources)
        {
            var resourceReservations = existing.Where(reservation =>
                    reservation.ResourceType == resource.ResourceType
                    && reservation.ResourceId == resource.ResourceId)
                .ToArray();
            var relevant = resourceReservations
                .Where(reservation => reservation.Status == ResourceReservationStatus.ACTIVE
                    || (candidate.PlannedStartAt < reservation.PlannedEndAt
                        && reservation.PlannedStartAt < candidate.PlannedEndAt))
                .Concat(resourceReservations
                    .Where(reservation => reservation.PlannedEndAt <= candidate.PlannedStartAt)
                    .OrderByDescending(reservation => reservation.PlannedEndAt)
                    .ThenByDescending(reservation => reservation.Id)
                    .Take(1))
                .Concat(resourceReservations
                    .Where(reservation => reservation.PlannedStartAt >= candidate.PlannedEndAt)
                    .OrderBy(reservation => reservation.PlannedStartAt)
                    .ThenBy(reservation => reservation.Id)
                    .Take(1))
                .DistinctBy(reservation => reservation.Id)
                .OrderBy(reservation => reservation.PlannedStartAt)
                .ToArray();

            var resourceConflicts = new List<ResourceAvailabilityConflict>();
            foreach (var reservation in relevant)
            {
                var conflictingSourceType = reservation.TripId.HasValue
                    ? AssignmentSourceType.TRIP
                    : AssignmentSourceType.SHUTTLE_TRIP;
                var conflictingSourceId = reservation.TripId ?? reservation.ShuttleTripId!.Value;
                var conflict = await CompareAsync(
                    assignment,
                    ToAssignment(reservation),
                    resource,
                    conflictingSourceType,
                    conflictingSourceId,
                    cancellationToken);
                AddConflict(resourceConflicts, conflict);
                if (conflicts.Count + resourceConflicts.Count > MaxConflicts)
                {
                    foreach (var item in resourceConflicts)
                    {
                        AddConflict(conflicts, item);
                    }

                    return ToResult(conflicts);
                }
            }

            await InvalidateEarliestStartsThatCannotFitBeforeNextAsync(
                candidate,
                resourceReservations,
                resourceConflicts,
                cancellationToken);
            foreach (var conflict in resourceConflicts)
            {
                AddConflict(conflicts, conflict);
            }
        }

        return ToResult(conflicts);
    }

    public async Task ReserveTripAsync(
        Domain.Entities.Trip trip,
        CancellationToken cancellationToken = default)
    {
        var candidate = await BuildTripCandidateAsync(trip, excludedTripId: trip.Id, cancellationToken);
        var existingResources = await db.ResourceReservations.AsNoTracking()
            .Where(item => item.TripId == trip.Id
                && (item.Status == ResourceReservationStatus.RESERVED
                    || item.Status == ResourceReservationStatus.ACTIVE))
            .Select(item => new AvailabilityResource(item.ResourceType, item.ResourceRole, item.ResourceId))
            .ToArrayAsync(cancellationToken);
        await AcquireResourceLocksAsync(candidate.Resources.Concat(existingResources).ToArray(), cancellationToken);
        var availability = await CheckCandidateAsync(candidate, acquireLocks: false, cancellationToken);
        ThrowIfUnavailable(availability, AssignmentSourceType.TRIP);
        await ReplaceTripReservationsAsync(trip.Id, candidate, cancellationToken);
    }

    public async Task ReserveShuttleTripAsync(
        ShuttleTrip shuttleTrip,
        IReadOnlyList<Guid> orderedBookingIds,
        CancellationToken cancellationToken = default)
    {
        var input = new ShuttleAvailabilityInput(
            shuttleTrip.OperatorId,
            shuttleTrip.MainTripId,
            shuttleTrip.Direction,
            shuttleTrip.DriverUserId,
            shuttleTrip.VehicleId,
            shuttleTrip.ScheduledDepartureTime,
            shuttleTrip.ScheduledEndTime,
            orderedBookingIds,
            shuttleTrip.Id);
        var candidate = await BuildShuttleCandidateAsync(input, cancellationToken) with
        {
            SourceId = shuttleTrip.Id,
            ExcludedShuttleTripId = shuttleTrip.Id,
        };
        var existingResources = await db.ResourceReservations.AsNoTracking()
            .Where(item => item.ShuttleTripId == shuttleTrip.Id
                && (item.Status == ResourceReservationStatus.RESERVED
                    || item.Status == ResourceReservationStatus.ACTIVE))
            .Select(item => new AvailabilityResource(item.ResourceType, item.ResourceRole, item.ResourceId))
            .ToArrayAsync(cancellationToken);
        await AcquireResourceLocksAsync(candidate.Resources.Concat(existingResources).ToArray(), cancellationToken);
        var availability = await CheckCandidateAsync(candidate, acquireLocks: false, cancellationToken);
        ThrowIfUnavailable(availability, AssignmentSourceType.SHUTTLE_TRIP);
        await ReplaceShuttleReservationsAsync(shuttleTrip.Id, candidate, cancellationToken);
    }

    public async Task RefreshTripAsync(
        Domain.Entities.Trip trip,
        CancellationToken cancellationToken = default) =>
        await ReserveTripAsync(trip, cancellationToken);

    public async Task RefreshTripsAsync(
        IReadOnlyCollection<Domain.Entities.Trip> trips,
        CancellationToken cancellationToken = default)
    {
        var allTrips = trips.DistinctBy(trip => trip.Id).ToArray();
        if (allTrips.Length == 0)
        {
            return;
        }

        var excludedTripIds = allTrips.Select(trip => trip.Id).ToArray();
        var candidates = new List<(Domain.Entities.Trip Trip, ResourceAvailabilityCandidate Candidate)>();
        foreach (var trip in allTrips.Where(trip => trip.Status is TripStatus.SCHEDULED or TripStatus.BOARDING))
        {
            candidates.Add((trip, await BuildTripCandidateAsync(trip, trip.Id, cancellationToken)));
        }

        var existingResources = await db.ResourceReservations.AsNoTracking()
            .Where(item => item.TripId.HasValue
                && excludedTripIds.Contains(item.TripId.Value)
                && (item.Status == ResourceReservationStatus.RESERVED
                    || item.Status == ResourceReservationStatus.ACTIVE))
            .Select(item => new AvailabilityResource(item.ResourceType, item.ResourceRole, item.ResourceId))
            .ToArrayAsync(cancellationToken);
        await AcquireResourceLocksAsync(
            candidates.SelectMany(item => item.Candidate.Resources).Concat(existingResources).ToArray(),
            cancellationToken);

        foreach (var item in candidates)
        {
            var availability = await CheckCandidateAsync(
                item.Candidate,
                acquireLocks: false,
                excludedTripIds,
                cancellationToken);
            ThrowIfUnavailable(availability, AssignmentSourceType.TRIP);
        }

        for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            for (var existingIndex = 0; existingIndex < candidateIndex; existingIndex++)
            {
                var candidate = candidates[candidateIndex].Candidate;
                var existing = candidates[existingIndex].Candidate;
                foreach (var resource in candidate.Resources)
                {
                    if (!existing.Resources.Any(item =>
                            item.ResourceType == resource.ResourceType
                            && item.ResourceId == resource.ResourceId))
                    {
                        continue;
                    }

                    var conflict = await CompareAsync(
                        ToAssignment(candidate),
                        ToAssignment(existing),
                        resource,
                        AssignmentSourceType.TRIP,
                        existing.SourceId!.Value,
                        cancellationToken);
                    if (conflict is not null)
                    {
                        ThrowConflict(AssignmentSourceType.TRIP, conflict);
                    }
                }
            }
        }

        foreach (var item in candidates)
        {
            await ReplaceTripReservationsAsync(item.Trip.Id, item.Candidate, cancellationToken);
        }
    }

    public Task ActivateTripAsync(Guid tripId, DateTimeOffset activatedAt, CancellationToken cancellationToken = default) =>
        TransitionTripReservationsAsync(tripId, activatedAt, ReservationTransition.Activate, cancellationToken);

    public Task ReleaseTripAsync(Guid tripId, DateTimeOffset releasedAt, CancellationToken cancellationToken = default) =>
        TransitionTripReservationsAsync(tripId, releasedAt, ReservationTransition.Release, cancellationToken);

    public Task CancelTripAsync(Guid tripId, DateTimeOffset cancelledAt, CancellationToken cancellationToken = default) =>
        TransitionTripReservationsAsync(tripId, cancelledAt, ReservationTransition.Cancel, cancellationToken);

    public Task ActivateShuttleTripAsync(Guid shuttleTripId, DateTimeOffset activatedAt, CancellationToken cancellationToken = default) =>
        TransitionShuttleReservationsAsync(shuttleTripId, activatedAt, ReservationTransition.Activate, cancellationToken);

    public Task ReleaseShuttleTripAsync(Guid shuttleTripId, DateTimeOffset releasedAt, CancellationToken cancellationToken = default) =>
        TransitionShuttleReservationsAsync(shuttleTripId, releasedAt, ReservationTransition.Release, cancellationToken);

    public Task CancelShuttleTripAsync(Guid shuttleTripId, DateTimeOffset cancelledAt, CancellationToken cancellationToken = default) =>
        TransitionShuttleReservationsAsync(shuttleTripId, cancelledAt, ReservationTransition.Cancel, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, (VehicleAssignmentProjection? Current, VehicleAssignmentProjection? Next)>>
        GetVehicleAssignmentsAsync(
            Guid operatorId,
            IReadOnlyCollection<Guid> vehicleIds,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
    {
        var distinctIds = vehicleIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (distinctIds.Length == 0)
        {
            return new Dictionary<Guid, (VehicleAssignmentProjection?, VehicleAssignmentProjection?)>();
        }

        var reservations = await db.ResourceReservations
            .AsNoTracking()
            .Where(reservation => reservation.OperatorId == operatorId
                && reservation.ResourceType == ResourceReservationType.VEHICLE
                && distinctIds.Contains(reservation.ResourceId)
                && (reservation.Status == ResourceReservationStatus.ACTIVE
                    || reservation.Status == ResourceReservationStatus.RESERVED))
            .OrderBy(reservation => reservation.PlannedStartAt)
            .ToArrayAsync(cancellationToken);
        var sourceTripIds = reservations.Where(item => item.TripId.HasValue).Select(item => item.TripId!.Value).ToArray();
        var sourceShuttleIds = reservations.Where(item => item.ShuttleTripId.HasValue).Select(item => item.ShuttleTripId!.Value).ToArray();
        var drivers = await db.ResourceReservations
            .AsNoTracking()
            .Where(reservation => reservation.ResourceRole == ResourceReservationRole.DRIVER
                && ((reservation.TripId.HasValue && sourceTripIds.Contains(reservation.TripId.Value))
                    || (reservation.ShuttleTripId.HasValue && sourceShuttleIds.Contains(reservation.ShuttleTripId.Value))))
            .ToArrayAsync(cancellationToken);

        return distinctIds.ToDictionary(
            vehicleId => vehicleId,
            vehicleId =>
            {
                var vehicleReservations = reservations.Where(item => item.ResourceId == vehicleId).ToArray();
                var current = vehicleReservations
                    .Where(item => item.Status == ResourceReservationStatus.ACTIVE)
                    .OrderBy(item => item.ActivatedAt)
                    .Select(item => ToProjection(item, drivers))
                    .FirstOrDefault();
                var next = vehicleReservations
                    .Where(item => item.Status == ResourceReservationStatus.RESERVED
                        && item.PlannedEndAt > now)
                    .OrderBy(item => item.PlannedStartAt)
                    .Select(item => ToProjection(item, drivers))
                    .FirstOrDefault();
                return (current, next);
            });
    }

    private async Task CompareScheduleWithConcreteReservationsAsync(
        DriverScheduleAvailabilityInput input,
        RoutePlan candidateRoute,
        IReadOnlyList<AvailabilityResource> candidateResources,
        List<ResourceAvailabilityConflict> conflicts,
        CancellationToken cancellationToken)
    {
        var resourceIds = candidateResources.Select(item => item.ResourceId).Distinct().ToArray();
        var excludedTripIds = input.ExcludePendingTripsFromSchedule && input.ExcludeScheduleId.HasValue
            ? await db.Trips.AsNoTracking()
                .Where(trip => trip.DriverScheduleId == input.ExcludeScheduleId.Value
                    && (trip.Status == TripStatus.SCHEDULED || trip.Status == TripStatus.BOARDING))
                .Select(trip => trip.Id)
                .ToArrayAsync(cancellationToken)
            : [];
        var fromUtc = BusinessTime.ToUtc(input.ValidFrom, TimeOnly.MinValue);
        var query = db.ResourceReservations.AsNoTracking()
            .Where(reservation => resourceIds.Contains(reservation.ResourceId)
                && (reservation.Status == ResourceReservationStatus.RESERVED
                    || reservation.Status == ResourceReservationStatus.ACTIVE)
                && (!reservation.TripId.HasValue || !excludedTripIds.Contains(reservation.TripId.Value))
                && reservation.PlannedEndAt >= fromUtc);
        if (input.ValidUntil.HasValue)
        {
            var toUtc = BusinessTime.ToUtc(input.ValidUntil.Value.AddDays(1), TimeOnly.MinValue);
            query = query.Where(reservation => reservation.PlannedStartAt < toUtc);
        }

        var reservations = await query.OrderBy(item => item.PlannedStartAt).ToArrayAsync(cancellationToken);
        foreach (var reservation in reservations)
        {
            var localDate = BusinessTime.ToLocalDate(reservation.PlannedStartAt);
            for (var offset = -1; offset <= 1; offset++)
            {
                var date = localDate.AddDays(offset);
                if (date < input.ValidFrom
                    || (input.ValidUntil.HasValue && date > input.ValidUntil.Value)
                    || !input.DayOfWeek.Contains(BusinessTime.ToIsoDayOfWeek(date)))
                {
                    continue;
                }

                var occurrence = BuildOccurrence(date, input.DepartureTime, candidateRoute);
                foreach (var resource in candidateResources.Where(resource =>
                             resource.ResourceType == reservation.ResourceType
                             && resource.ResourceId == reservation.ResourceId))
                {
                    var sourceType = reservation.TripId.HasValue
                        ? AssignmentSourceType.TRIP
                        : AssignmentSourceType.SHUTTLE_TRIP;
                    var conflict = await CompareAsync(
                        occurrence,
                        ToAssignment(reservation),
                        resource,
                        sourceType,
                        reservation.TripId ?? reservation.ShuttleTripId!.Value,
                        cancellationToken);
                    AddConflict(conflicts, conflict);
                    if (conflicts.Count > MaxConflicts)
                    {
                        return;
                    }
                }
            }
        }
    }

    private async Task<ResourceAvailabilityCandidate> BuildTripCandidateAsync(
        Domain.Entities.Trip trip,
        Guid? excludedTripId,
        CancellationToken cancellationToken)
    {
        var route = await db.Routes.AsNoTracking().SingleOrDefaultAsync(item => item.Id == trip.RouteId, cancellationToken)
            ?? throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Trip route was not found.");
        var destinationStationId = route.DestinationStationId;
        if (trip.AlternativeRouteId.HasValue)
        {
            destinationStationId = await db.AlternativeRoutes.AsNoTracking()
                .Where(item => item.Id == trip.AlternativeRouteId.Value)
                .Select(item => item.DestinationStationId)
                .SingleOrDefaultAsync(cancellationToken);
            if (destinationStationId == Guid.Empty)
            {
                throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Trip alternative route was not found.");
            }
        }

        var stations = await db.Stations.AsNoTracking()
            .Where(item => item.Id == route.OriginStationId || item.Id == destinationStationId)
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        if (!stations.TryGetValue(route.OriginStationId, out var origin)
            || !stations.TryGetValue(destinationStationId, out var destination))
        {
            throw new CodedNotFoundException("STATION_NOT_FOUND", "Trip endpoint Station was not found.");
        }

        return new ResourceAvailabilityCandidate(
            trip.OperatorId,
            AssignmentSourceType.TRIP,
            trip.Id,
            excludedTripId,
            null,
            trip.DepartureDateTime,
            trip.EstimatedArrivalTime,
            ToLocation(origin),
            ToLocation(destination),
            BuildResources(trip.DriverUserId, trip.AssistantUserId, trip.VehicleId));
    }

    private async Task<ResourceAvailabilityCandidate> BuildShuttleCandidateAsync(
        ShuttleAvailabilityInput input,
        CancellationToken cancellationToken)
    {
        if (input.Direction is not (ShuttleTrip.InboundDirection or ShuttleTrip.OutboundDirection))
        {
            throw new CodedValidationException("VALIDATION_ERROR", "Shuttle direction is invalid.");
        }

        if (input.OrderedBookingIds.Count == 0)
        {
            throw new CodedValidationException("VALIDATION_ERROR", "orderedBookingIds must not be empty.");
        }

        var mainTrip = await db.Trips.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == input.MainTripId && item.OperatorId == input.OperatorId, cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Main trip was not found.");
        var route = await db.Routes.AsNoTracking().SingleAsync(item => item.Id == mainTrip.RouteId, cancellationToken);
        var stationId = input.Direction == ShuttleTrip.InboundDirection
            ? route.OriginStationId
            : route.DestinationStationId;
        var station = await db.Stations.AsNoTracking().SingleOrDefaultAsync(item => item.Id == stationId, cancellationToken)
            ?? throw new CodedNotFoundException("STATION_NOT_FOUND", "Shuttle station was not found.");

        var selectedIds = input.OrderedBookingIds.Distinct().ToArray();
        var manifests = await db.ShuttlePassengers.AsNoTracking()
            .Where(item => item.MainTripId == input.MainTripId
                && item.Direction == input.Direction
                && item.BookingId.HasValue
                && selectedIds.Contains(item.BookingId.Value))
            .OrderBy(item => item.CreatedAt)
            .ToArrayAsync(cancellationToken);
        var byBooking = manifests
            .GroupBy(item => item.BookingId!.Value)
            .ToDictionary(group => group.Key, group => group.First());
        if (input.OrderedBookingIds.Any(id => !byBooking.ContainsKey(id)))
        {
            throw new CodedConflictException("SHUTTLE_REQUEST_SET_CHANGED", "One or more selected Booking groups changed.");
        }

        var first = byBooking[input.OrderedBookingIds[0]];
        var last = byBooking[input.OrderedBookingIds[^1]];
        var stationLocation = ToLocation(station);
        var start = input.Direction == ShuttleTrip.InboundDirection
            ? new ResourceLocationSnapshot(null, first.PickupLat, first.PickupLng)
            : stationLocation;
        var end = input.Direction == ShuttleTrip.InboundDirection
            ? stationLocation
            : new ResourceLocationSnapshot(null, last.PickupLat, last.PickupLng);

        return new ResourceAvailabilityCandidate(
            input.OperatorId,
            AssignmentSourceType.SHUTTLE_TRIP,
            input.ExcludeShuttleTripId,
            null,
            input.ExcludeShuttleTripId,
            input.ScheduledDepartureTime,
            input.ScheduledEndTime,
            start,
            end,
            BuildResources(input.DriverUserId, null, input.VehicleId));
    }

    private async Task<RoutePlan> LoadRoutePlanAsync(Guid routeId, Guid operatorId, CancellationToken cancellationToken)
    {
        var route = await db.Routes.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == routeId && item.OperatorId == operatorId, cancellationToken)
            ?? throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Route was not found.");
        var duration = route.EstimatedDurationMinutes;
        if (duration is not > 0)
        {
            duration = await db.RouteStops.AsNoTracking()
                .Where(item => item.RouteId == routeId)
                .Select(item => (int?)item.EstimatedDurationFromOriginMinutes)
                .MaxAsync(cancellationToken);
        }

        if (duration is not > 0)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Route estimated duration is required for resource availability.",
                [new ValidationError("estimatedArrivalTime", "Route duration or route-stop duration is required.")]);
        }

        var stations = await db.Stations.AsNoTracking()
            .Where(item => item.Id == route.OriginStationId || item.Id == route.DestinationStationId)
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        if (!stations.TryGetValue(route.OriginStationId, out var origin)
            || !stations.TryGetValue(route.DestinationStationId, out var destination))
        {
            throw new CodedNotFoundException("STATION_NOT_FOUND", "Route endpoint Station was not found.");
        }

        return new RoutePlan(duration.Value, ToLocation(origin), ToLocation(destination));
    }

    private async Task ReplaceTripReservationsAsync(
        Guid tripId,
        ResourceAvailabilityCandidate candidate,
        CancellationToken cancellationToken)
    {
        var existing = await db.ResourceReservations
            .Where(item => item.TripId == tripId)
            .ToArrayAsync(cancellationToken);
        await ReplaceReservationsAsync(existing, candidate, tripId, shuttleTripId: null, cancellationToken);
    }

    private async Task ReplaceShuttleReservationsAsync(
        Guid shuttleTripId,
        ResourceAvailabilityCandidate candidate,
        CancellationToken cancellationToken)
    {
        var existing = await db.ResourceReservations
            .Where(item => item.ShuttleTripId == shuttleTripId)
            .ToArrayAsync(cancellationToken);
        await ReplaceReservationsAsync(existing, candidate, tripId: null, shuttleTripId, cancellationToken);
    }

    private Task ReplaceReservationsAsync(
        IReadOnlyCollection<ResourceReservation> existing,
        ResourceAvailabilityCandidate candidate,
        Guid? tripId,
        Guid? shuttleTripId,
        CancellationToken cancellationToken)
    {
        foreach (var stale in existing.Where(item => candidate.Resources.All(resource =>
                     resource.ResourceRole != item.ResourceRole)))
        {
            stale.Cancel(clock.UtcNow);
        }

        foreach (var resource in candidate.Resources)
        {
            var reservation = existing.FirstOrDefault(item => item.ResourceRole == resource.ResourceRole);
            if (reservation is null)
            {
                reservation = tripId.HasValue
                    ? ResourceReservation.CreateForTrip(
                        candidate.OperatorId,
                        resource.ResourceType,
                        resource.ResourceRole,
                        resource.ResourceId,
                        tripId.Value,
                        candidate.PlannedStartAt,
                        candidate.PlannedEndAt,
                        candidate.StartLocation.StationId,
                        candidate.EndLocation.StationId,
                        candidate.StartLocation.Latitude,
                        candidate.StartLocation.Longitude,
                        candidate.EndLocation.Latitude,
                        candidate.EndLocation.Longitude)
                    : ResourceReservation.CreateForShuttleTrip(
                        candidate.OperatorId,
                        resource.ResourceType,
                        resource.ResourceRole,
                        resource.ResourceId,
                        shuttleTripId!.Value,
                        candidate.PlannedStartAt,
                        candidate.PlannedEndAt,
                        candidate.StartLocation.StationId,
                        candidate.EndLocation.StationId,
                        candidate.StartLocation.Latitude,
                        candidate.StartLocation.Longitude,
                        candidate.EndLocation.Latitude,
                        candidate.EndLocation.Longitude);
                db.ResourceReservations.Add(reservation);
            }
            else
            {
                reservation.UpdatePlan(
                    resource.ResourceId,
                    candidate.PlannedStartAt,
                    candidate.PlannedEndAt,
                    candidate.StartLocation.StationId,
                    candidate.EndLocation.StationId,
                    candidate.StartLocation.Latitude,
                    candidate.StartLocation.Longitude,
                    candidate.EndLocation.Latitude,
                    candidate.EndLocation.Longitude);
            }
        }

        _ = cancellationToken;
        return Task.CompletedTask;
    }

    private async Task TransitionTripReservationsAsync(
        Guid tripId,
        DateTimeOffset at,
        ReservationTransition transition,
        CancellationToken cancellationToken)
    {
        var reservations = await db.ResourceReservations.Where(item => item.TripId == tripId).ToArrayAsync(cancellationToken);
        if (reservations.Length == 0 && transition == ReservationTransition.Activate)
        {
            var trip = await db.Trips.SingleOrDefaultAsync(item => item.Id == tripId, cancellationToken)
                ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
            await ReserveTripAsync(trip, cancellationToken);
            reservations = db.ResourceReservations.Local.Where(item => item.TripId == tripId).ToArray();
        }

        await TransitionReservationsAsync(reservations, at, transition, AssignmentSourceType.TRIP, cancellationToken);
    }

    private async Task TransitionShuttleReservationsAsync(
        Guid shuttleTripId,
        DateTimeOffset at,
        ReservationTransition transition,
        CancellationToken cancellationToken)
    {
        var reservations = await db.ResourceReservations.Where(item => item.ShuttleTripId == shuttleTripId).ToArrayAsync(cancellationToken);
        if (reservations.Length == 0 && transition == ReservationTransition.Activate)
        {
            var shuttleTrip = await db.ShuttleTrips.SingleOrDefaultAsync(item => item.Id == shuttleTripId, cancellationToken)
                ?? throw new CodedNotFoundException("SHUTTLE_TRIP_NOT_FOUND", "Shuttle trip was not found.");
            var manifests = await db.ShuttlePassengers.AsNoTracking()
                .Where(item => item.ShuttleTripId == shuttleTripId && item.BookingId.HasValue)
                .OrderBy(item => item.PickupOrder)
                .ThenBy(item => item.CreatedAt)
                .ToArrayAsync(cancellationToken);
            var orderedBookingIds = manifests
                .GroupBy(item => item.BookingId!.Value)
                .Select(group => group.First())
                .OrderBy(item => item.PickupOrder)
                .ThenBy(item => item.CreatedAt)
                .Select(item => item.BookingId!.Value)
                .ToArray();
            await ReserveShuttleTripAsync(shuttleTrip, orderedBookingIds, cancellationToken);
            reservations = db.ResourceReservations.Local.Where(item => item.ShuttleTripId == shuttleTripId).ToArray();
        }
        await TransitionReservationsAsync(reservations, at, transition, AssignmentSourceType.SHUTTLE_TRIP, cancellationToken);
    }

    private async Task TransitionReservationsAsync(
        IReadOnlyCollection<ResourceReservation> reservations,
        DateTimeOffset at,
        ReservationTransition transition,
        AssignmentSourceType sourceType,
        CancellationToken cancellationToken)
    {
        if (reservations.Count > 0)
        {
            await AcquireResourceLocksAsync(
                reservations.Select(item => new AvailabilityResource(item.ResourceType, item.ResourceRole, item.ResourceId)).ToArray(),
                cancellationToken);
        }

        if (transition == ReservationTransition.Activate)
        {
            var resourceIds = reservations.Select(item => item.ResourceId).Distinct().ToArray();
            var ownIds = reservations.Select(item => item.Id).ToArray();
            var activeCandidates = await db.ResourceReservations.AsNoTracking()
                .Where(item => resourceIds.Contains(item.ResourceId)
                    && !ownIds.Contains(item.Id)
                    && item.Status == ResourceReservationStatus.ACTIVE)
                .ToArrayAsync(cancellationToken);
            var activeConflict = activeCandidates.FirstOrDefault(candidate => reservations.Any(own =>
                own.ResourceType == candidate.ResourceType
                && own.ResourceId == candidate.ResourceId));
            if (activeConflict is not null)
            {
                var role = reservations.First(item =>
                    item.ResourceType == activeConflict.ResourceType
                    && item.ResourceId == activeConflict.ResourceId).ResourceRole;
                ThrowConflict(
                    sourceType,
                    new ResourceAvailabilityConflict(
                        role.ToString(),
                        activeConflict.ResourceId,
                        AvailabilityConflictReason.RESOURCE_ACTIVE.ToString(),
                        activeConflict.TripId.HasValue ? AssignmentSourceType.TRIP.ToString() : AssignmentSourceType.SHUTTLE_TRIP.ToString(),
                        activeConflict.TripId ?? activeConflict.ShuttleTripId!.Value,
                        at,
                        activeConflict.PlannedEndAt,
                        null,
                        0,
                        TurnaroundMinutes));
            }
        }

        foreach (var reservation in reservations)
        {
            switch (transition)
            {
                case ReservationTransition.Activate:
                    reservation.Activate(at);
                    break;
                case ReservationTransition.Release:
                    reservation.Release(at);
                    break;
                case ReservationTransition.Cancel:
                    reservation.Cancel(at);
                    break;
            }
        }
    }

    private async Task AcquireResourceLocksAsync(
        IReadOnlyCollection<AvailabilityResource> resources,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A caller-owned transaction is required for resource availability locking.");
        }

        foreach (var key in resources
                     .Select(item => $"resource-availability:{item.ResourceType}:{item.ResourceId:D}")
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))",
                cancellationToken);
        }
    }

    private async Task<ResourceAvailabilityConflict?> CompareAsync(
        PlannedAssignment candidate,
        PlannedAssignment existing,
        AvailabilityResource resource,
        AssignmentSourceType conflictingSourceType,
        Guid conflictingSourceId,
        CancellationToken cancellationToken)
    {
        var travel = existing.EndAt <= candidate.StartAt
            || (candidate.StartAt < existing.EndAt && existing.StartAt < candidate.EndAt)
            ? await GetTravelMinutesAsync(existing.EndLocation, candidate.StartLocation, cancellationToken)
            : await GetTravelMinutesAsync(candidate.EndLocation, existing.StartLocation, cancellationToken);

        return ResourceAvailabilityPolicy.Compare(
            candidate.StartAt,
            candidate.EndAt,
            existing.StartAt,
            existing.EndAt,
            resource,
            conflictingSourceType,
            conflictingSourceId,
            travel);
    }

    private async Task InvalidateEarliestStartsThatCannotFitBeforeNextAsync(
        ResourceAvailabilityCandidate candidate,
        IReadOnlyCollection<ResourceReservation> resourceReservations,
        List<ResourceAvailabilityConflict> conflicts,
        CancellationToken cancellationToken)
    {
        if (conflicts.All(conflict => !conflict.EarliestFeasibleStartAt.HasValue))
        {
            return;
        }

        var next = resourceReservations
            .Where(reservation => reservation.PlannedStartAt >= candidate.PlannedEndAt)
            .OrderBy(reservation => reservation.PlannedStartAt)
            .ThenBy(reservation => reservation.Id)
            .FirstOrDefault();
        if (next is null)
        {
            return;
        }

        var travelMinutes = await GetTravelMinutesAsync(
            candidate.EndLocation,
            ToAssignment(next).StartLocation,
            cancellationToken);
        var duration = candidate.PlannedEndAt - candidate.PlannedStartAt;
        for (var index = 0; index < conflicts.Count; index++)
        {
            var earliest = conflicts[index].EarliestFeasibleStartAt;
            if (earliest.HasValue
                && !ResourceAvailabilityPolicy.CanFitBeforeNext(
                    earliest.Value,
                    duration,
                    travelMinutes,
                    next.PlannedStartAt))
            {
                conflicts[index] = conflicts[index] with { EarliestFeasibleStartAt = null };
            }
        }
    }

    private async Task<int> GetTravelMinutesAsync(
        ResourceLocationSnapshot origin,
        ResourceLocationSnapshot destination,
        CancellationToken cancellationToken)
    {
        if (origin.StationId.HasValue
            && destination.StationId.HasValue
            && origin.StationId.Value == destination.StationId.Value)
        {
            return 0;
        }

        if (origin.Latitude.HasValue
            && origin.Longitude.HasValue
            && destination.Latitude.HasValue
            && destination.Longitude.HasValue
            && origin.Latitude == destination.Latitude
            && origin.Longitude == destination.Longitude)
        {
            return 0;
        }

        if (!origin.Latitude.HasValue
            || !origin.Longitude.HasValue
            || !destination.Latitude.HasValue
            || !destination.Longitude.HasValue)
        {
            throw new ResourceTravelTimeUnavailableException(
                "Both assignment endpoints require coordinates for reposition validation.");
        }

        var key = new LocationPair(
            origin.Latitude.Value,
            origin.Longitude.Value,
            destination.Latitude.Value,
            destination.Longitude.Value);
        if (travelTimeCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var result = await travelTimeClient.CalculateAsync(
            key.OriginLatitude,
            key.OriginLongitude,
            key.DestinationLatitude,
            key.DestinationLongitude,
            cancellationToken);
        if (!result.IsAvailable || !result.DurationMinutes.HasValue)
        {
            throw new ResourceTravelTimeUnavailableException(
                result.FailureMessage ?? "Google Routes returned no reposition duration.");
        }

        travelTimeCache[key] = result.DurationMinutes.Value;
        return result.DurationMinutes.Value;
    }

    private static void ThrowIfUnavailable(ResourceAvailabilityResult result, AssignmentSourceType sourceType)
    {
        if (!result.Available)
        {
            ThrowConflict(sourceType, result.Conflicts[0]);
        }
    }

    private static void ThrowConflict(AssignmentSourceType sourceType, ResourceAvailabilityConflict conflict)
    {
        var isVehicle = string.Equals(conflict.ResourceRole, ResourceReservationRole.VEHICLE.ToString(), StringComparison.Ordinal);
        var code = sourceType == AssignmentSourceType.SHUTTLE_TRIP
            ? isVehicle ? "SHUTTLE_VEHICLE_CONFLICT" : "SHUTTLE_DRIVER_CONFLICT"
            : isVehicle ? "TRIP_VEHICLE_CONFLICT" : "TRIP_DRIVER_CONFLICT";
        throw new CodedConflictException(
            code,
            $"{conflict.ResourceRole} has an unavailable assignment window.",
            [
                new ValidationError("conflictReason", conflict.Reason),
                new ValidationError("resourceRole", conflict.ResourceRole),
                new ValidationError("resourceId", conflict.ResourceId.ToString("D")),
                new ValidationError("conflictingSourceType", conflict.ConflictingSourceType),
                new ValidationError("conflictingSourceId", conflict.ConflictingSourceId.ToString("D")),
                new ValidationError("blockingUntil", conflict.BlockingUntil.ToString("O")),
            ]);
    }

    private static void AddConflict(
        ICollection<ResourceAvailabilityConflict> conflicts,
        ResourceAvailabilityConflict? conflict)
    {
        if (conflict is not null && !conflicts.Contains(conflict))
        {
            conflicts.Add(conflict);
        }
    }

    private static ResourceAvailabilityResult ToResult(IReadOnlyCollection<ResourceAvailabilityConflict> conflicts)
    {
        var ordered = conflicts
            .OrderBy(item => item.SampleRequestedStartAt)
            .ThenBy(item => item.ResourceRole, StringComparer.Ordinal)
            .ThenBy(item => item.ResourceId)
            .Take(MaxConflicts)
            .ToArray();
        return new ResourceAvailabilityResult(
            ordered.Length == 0,
            TurnaroundMinutes,
            ordered,
            conflicts.Count > MaxConflicts);
    }

    private static IReadOnlyList<AvailabilityResource> BuildResources(
        Guid driverUserId,
        Guid? assistantUserId,
        Guid? vehicleId)
    {
        var resources = new List<AvailabilityResource>
        {
            new(ResourceReservationType.CREW, ResourceReservationRole.DRIVER, driverUserId),
        };
        if (assistantUserId.HasValue)
        {
            resources.Add(new AvailabilityResource(
                ResourceReservationType.CREW,
                ResourceReservationRole.ASSISTANT,
                assistantUserId.Value));
        }

        if (vehicleId.HasValue)
        {
            resources.Add(new AvailabilityResource(
                ResourceReservationType.VEHICLE,
                ResourceReservationRole.VEHICLE,
                vehicleId.Value));
        }

        return resources;
    }

    private static List<PlannedAssignment> BuildOccurrences(
        DateOnly validFrom,
        DateOnly? validUntil,
        IReadOnlyCollection<int> days,
        TimeOnly departureTime,
        RoutePlan route,
        DateOnly windowStart,
        DateOnly windowEnd)
    {
        var occurrences = new List<PlannedAssignment>();
        var first = Max(validFrom, windowStart);
        var last = validUntil.HasValue ? Min(validUntil, windowEnd)!.Value : windowEnd;
        if (last < first)
        {
            return occurrences;
        }

        var normalizedDays = days.ToHashSet();
        for (var date = first; date <= last; date = date.AddDays(1))
        {
            if (normalizedDays.Contains(BusinessTime.ToIsoDayOfWeek(date)))
            {
                occurrences.Add(BuildOccurrence(date, departureTime, route));
            }
        }

        return occurrences;
    }

    private static PlannedAssignment BuildOccurrence(DateOnly date, TimeOnly time, RoutePlan route)
    {
        var start = BusinessTime.ToUtc(date, time);
        return new PlannedAssignment(
            start,
            start.AddMinutes(route.DurationMinutes),
            route.StartLocation,
            route.EndLocation);
    }

    private static PlannedAssignment ToAssignment(ResourceAvailabilityCandidate candidate) => new(
        candidate.PlannedStartAt,
        candidate.PlannedEndAt,
        candidate.StartLocation,
        candidate.EndLocation);

    private static PlannedAssignment ToAssignment(ResourceReservation reservation) => new(
        reservation.PlannedStartAt,
        reservation.PlannedEndAt,
        new ResourceLocationSnapshot(
            reservation.StartStationId,
            reservation.StartLatitude,
            reservation.StartLongitude),
        new ResourceLocationSnapshot(
            reservation.EndStationId,
            reservation.EndLatitude,
            reservation.EndLongitude));

    private static ResourceLocationSnapshot ToLocation(Station station) =>
        new(station.Id, station.Latitude, station.Longitude);

    private static VehicleAssignmentProjection ToProjection(
        ResourceReservation vehicle,
        IReadOnlyCollection<ResourceReservation> drivers)
    {
        var driver = drivers.First(item =>
            item.TripId == vehicle.TripId
            && item.ShuttleTripId == vehicle.ShuttleTripId);
        return new VehicleAssignmentProjection(
            vehicle.TripId.HasValue ? AssignmentSourceType.TRIP.ToString() : AssignmentSourceType.SHUTTLE_TRIP.ToString(),
            vehicle.TripId ?? vehicle.ShuttleTripId!.Value,
            vehicle.Status.ToString(),
            driver.ResourceId,
            vehicle.PlannedStartAt,
            vehicle.PlannedEndAt,
            vehicle.StartStationId,
            vehicle.EndStationId);
    }

    private static IReadOnlyCollection<int> ParseDays(JsonElement json) =>
        json.Deserialize<int[]>() ?? [];

    private static void ValidateScheduleInput(DriverScheduleAvailabilityInput input)
    {
        if (input.DriverUserId == Guid.Empty
            || input.RouteId == Guid.Empty
            || input.DayOfWeek.Count == 0
            || input.DayOfWeek.Any(day => day is < 1 or > 7)
            || (input.ValidUntil.HasValue && input.ValidUntil.Value < input.ValidFrom))
        {
            throw new CodedValidationException("VALIDATION_ERROR", "DriverSchedule availability input is invalid.");
        }
    }

    private static void ValidateCandidate(ResourceAvailabilityCandidate candidate)
    {
        if (candidate.OperatorId == Guid.Empty
            || candidate.PlannedEndAt <= candidate.PlannedStartAt
            || candidate.Resources.Count == 0
            || candidate.Resources.Any(item => item.ResourceId == Guid.Empty))
        {
            throw new CodedValidationException("VALIDATION_ERROR", "Resource availability candidate is invalid.");
        }

        EnsureLocationCoordinates(candidate.StartLocation);
        EnsureLocationCoordinates(candidate.EndLocation);
    }

    private static void EnsureLocationCoordinates(ResourceLocationSnapshot location)
    {
        if (!location.Latitude.HasValue || !location.Longitude.HasValue)
        {
            throw new ResourceTravelTimeUnavailableException(
                "Every assignment endpoint requires coordinates for resource availability.");
        }
    }

    private static DateOnly Max(DateOnly left, DateOnly right) => left >= right ? left : right;

    private static DateOnly? Min(DateOnly? left, DateOnly? right)
    {
        if (!left.HasValue) return right;
        if (!right.HasValue) return left;
        return left.Value <= right.Value ? left : right;
    }

    private enum ReservationTransition
    {
        Activate,
        Release,
        Cancel,
    }

    private sealed record RoutePlan(
        int DurationMinutes,
        ResourceLocationSnapshot StartLocation,
        ResourceLocationSnapshot EndLocation);

    private sealed record PlannedAssignment(
        DateTimeOffset StartAt,
        DateTimeOffset EndAt,
        ResourceLocationSnapshot StartLocation,
        ResourceLocationSnapshot EndLocation);

    private readonly record struct LocationPair(
        decimal OriginLatitude,
        decimal OriginLongitude,
        decimal DestinationLatitude,
        decimal DestinationLongitude);
}
