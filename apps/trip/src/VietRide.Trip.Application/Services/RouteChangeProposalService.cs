using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Common.Geometry;
using VietRide.Trip.Application.Events;
using VietRide.Trip.Application.Features.AlternativeRoutes;
using VietRide.Trip.Application.Features.RouteChangeProposals;
using VietRide.Trip.Application.Features.Trips;
using VietRide.Trip.Domain.Constants;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Services;

public sealed class RouteChangeProposalService : IRouteChangeProposalService
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IRouteChangeProposalRepository proposals;
    private readonly ITripRepository trips;
    private readonly IAlternativeRouteRepository alternativeRoutes;
    private readonly IRouteRepository routes;
    private readonly IStationRepository stations;
    private readonly IOperatorStationRepository operatorStations;
    private readonly IStopRepository stops;
    private readonly IIncidentRepository incidents;
    private readonly ITripAuditLogRepository auditLogs;
    private readonly IBookingImpactClient bookingImpact;
    private readonly ITripRouteChangeService tripRouteChanges;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IUnitOfWork unitOfWork;
    private readonly IClock clock;

    public RouteChangeProposalService(
        IRouteChangeProposalRepository proposals,
        ITripRepository trips,
        IAlternativeRouteRepository alternativeRoutes,
        IRouteRepository routes,
        IStationRepository stations,
        IOperatorStationRepository operatorStations,
        IStopRepository stops,
        IIncidentRepository incidents,
        ITripAuditLogRepository auditLogs,
        IBookingImpactClient bookingImpact,
        ITripRouteChangeService tripRouteChanges,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        this.proposals = proposals;
        this.trips = trips;
        this.alternativeRoutes = alternativeRoutes;
        this.routes = routes;
        this.stations = stations;
        this.operatorStations = operatorStations;
        this.stops = stops;
        this.incidents = incidents;
        this.auditLogs = auditLogs;
        this.bookingImpact = bookingImpact;
        this.tripRouteChanges = tripRouteChanges;
        this.outbox = outbox;
        this.unitOfWork = unitOfWork;
        this.clock = clock;
    }

    public async Task<PagedResult<AlternativeRouteDto>> ListAlternativeRoutesForAssignedCrewAsync(
        Guid tripId,
        Guid userId,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var trip = await GetAssignedTripAsync(tripId, userId, cancellationToken);
        var resolvedPage = page ?? DefaultPage;
        var resolvedPageSize = pageSize ?? DefaultPageSize;
        var query = alternativeRoutes.QueryNoTracking()
            .Where(route => route.RouteId == trip.RouteId && route.IsActive);
        var total = await query.LongCountAsync(cancellationToken);
        var routes = await query
            .OrderBy(route => route.Name)
            .ThenBy(route => route.Id)
            .Skip((resolvedPage - 1) * resolvedPageSize)
            .Take(resolvedPageSize)
            .ToListAsync(cancellationToken);
        var result = new List<AlternativeRouteDto>(routes.Count);
        foreach (var route in routes)
        {
            var routeStops = await alternativeRoutes.ListStopsAsync(route.Id, cancellationToken);
            result.Add(AlternativeRouteMapper.ToDto(route, routeStops));
        }
        return PagedResult<AlternativeRouteDto>.Create(result, resolvedPage, resolvedPageSize, total);
    }

    public async Task<RouteChangeProposalDto> CreateAsync(
        Guid tripId,
        Guid userId,
        string type,
        Guid? alternativeRouteId,
        RouteChangeProposalSnapshotInput? customRoute,
        Guid? incidentId,
        string reason,
        CancellationToken cancellationToken)
    {
        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var proposalType = ParseType(type);
            if (proposalType == RouteChangeProposalType.EXISTING && alternativeRouteId.HasValue)
                await proposals.AcquireSourceCoordinationLockAsync(alternativeRouteId.Value, cancellationToken);
            var trip = await GetAssignedTripForProposalCreationAsync(tripId, userId, cancellationToken);
            EnsureTripEditable(trip);
            await ValidateIncidentAsync(incidentId, tripId, cancellationToken);

            AlternativeRoute? source = null;
            RouteChangeProposalSnapshotInput snapshot;
            if (proposalType == RouteChangeProposalType.EXISTING)
            {
                if (!alternativeRouteId.HasValue || customRoute is not null)
                    throw InvalidSnapshot("Existing proposals require alternativeRouteId and no customRoute.");
                source = await alternativeRoutes.AcquireOwnedByIdAsync(
                    trip.OperatorId,
                    alternativeRouteId.Value,
                    cancellationToken);
                if (source is null || !source.IsActive || source.RouteId != trip.RouteId)
                    throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Alternative route was not found.");
                snapshot = new RouteChangeProposalSnapshotInput(
                    source.Name,
                    source.Description,
                    source.DestinationStationId,
                    source.TotalDistanceKm,
                    source.EstimatedDurationMinutes,
                    source.PathPolyline,
                    (await alternativeRoutes.ListStopsAsync(source.Id, cancellationToken))
                        .Select(ToSnapshotStop)
                        .ToArray());
            }
            else
            {
                if (alternativeRouteId.HasValue || customRoute is null)
                    throw InvalidSnapshot("Custom proposals require customRoute and no alternativeRouteId.");
                await ValidateSnapshotAsync(customRoute, trip.OperatorId, trip.RouteId, cancellationToken);
                snapshot = customRoute;
            }

            RouteChangeProposal proposal;
            try
            {
                proposal = RouteChangeProposal.Create(
                    trip.Id,
                    trip.OperatorId,
                    userId,
                    proposalType,
                    source?.Id,
                    source?.UpdatedAt,
                    incidentId,
                    reason,
                    snapshot.Name,
                    snapshot.Description,
                    snapshot.DestinationStationId,
                    snapshot.TotalDistanceKm,
                    snapshot.EstimatedDurationMinutes,
                    snapshot.PathPolyline);
                foreach (var stop in snapshot.Stops)
                {
                    proposal.AddStop(RouteChangeProposalStop.Create(
                        proposal.Id,
                        stop.StopId,
                        stop.OrderIndex,
                        stop.EstimatedDurationFromOriginMinutes,
                        stop.DistanceFromOriginKm));
                }
            }
            catch (ArgumentException exception)
            {
                throw InvalidSnapshot(exception.Message);
            }

            await proposals.AddAsync(proposal, cancellationToken);
            await AddAuditAsync(proposal, TripAuditAction.RouteChangeProposalCreated, userId, clock.UtcNow, cancellationToken);
            await EnqueueProposalEventAsync(RouteChangeProposalIntegrationEvent.Created, proposal, userId, clock.UtcNow, cancellationToken);
            return RouteChangeProposalMapper.ToDto(proposal);
        }, cancellationToken);
    }

    public async Task<PagedResult<RouteChangeProposalDto>> ListForAssignedCrewAsync(Guid tripId, Guid userId, string? type, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        _ = await GetAssignedTripAsync(tripId, userId, cancellationToken);
        var resolvedPage = page ?? DefaultPage;
        var resolvedPageSize = pageSize ?? DefaultPageSize;
        var query = proposals.QueryWithStopsNoTracking().Where(proposal => proposal.TripId == tripId);
        if (!string.IsNullOrWhiteSpace(type))
        {
            if (!Enum.TryParse<RouteChangeProposalType>(type.Trim(), true, out var parsed))
                throw new ValidationException("Proposal type is invalid.", [new ValidationError("type", "Use EXISTING or CUSTOM.")]);
            query = query.Where(proposal => proposal.Type == parsed);
        }
        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(proposal => proposal.CreatedAt)
            .ThenBy(proposal => proposal.Id)
            .Skip((resolvedPage - 1) * resolvedPageSize)
            .Take(resolvedPageSize)
            .ToListAsync(cancellationToken);
        return PagedResult<RouteChangeProposalDto>.Create(
            rows.Select(RouteChangeProposalMapper.ToDto).ToArray(), resolvedPage, resolvedPageSize, total);
    }

    public async Task<PagedResult<RouteChangeProposalDto>> ListForOperatorAsync(
        Guid operatorId,
        Guid? tripId,
        string? status,
        string? type,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var query = proposals.QueryWithStopsNoTracking().Where(proposal => proposal.OperatorId == operatorId);
        if (tripId.HasValue) query = query.Where(proposal => proposal.TripId == tripId.Value);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<RouteChangeProposalStatus>(status.Trim(), true, out var parsed))
                throw new ValidationException("Proposal status is invalid.", [new ValidationError("status", "Use PENDING, APPROVED, REJECTED, SUPERSEDED, or EXPIRED.")]);
            query = query.Where(row => row.Status == parsed);
        }
        if (!string.IsNullOrWhiteSpace(type))
        {
            if (!Enum.TryParse<RouteChangeProposalType>(type.Trim(), true, out var parsed))
                throw new ValidationException("Proposal type is invalid.", [new ValidationError("type", "Use EXISTING or CUSTOM.")]);
            query = query.Where(row => row.Type == parsed);
        }
        var resolvedPage = page ?? DefaultPage;
        var resolvedPageSize = pageSize ?? DefaultPageSize;
        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query.OrderByDescending(proposal => proposal.CreatedAt)
            .ThenBy(proposal => proposal.Id)
            .Skip((resolvedPage - 1) * resolvedPageSize)
            .Take(resolvedPageSize)
            .ToListAsync(cancellationToken);
        return PagedResult<RouteChangeProposalDto>.Create(
            rows.Select(RouteChangeProposalMapper.ToDto).ToArray(), resolvedPage, resolvedPageSize, total);
    }

    public async Task<RouteChangeProposalDto> GetForOperatorAsync(Guid operatorId, Guid proposalId, CancellationToken cancellationToken)
    {
        return RouteChangeProposalMapper.ToDto(
            await proposals.GetOwnedByIdAsync(operatorId, proposalId, cancellationToken) ?? throw ProposalNotFound());
    }

    public async Task<ApproveRouteChangeProposalResponse> ApproveAsync(Guid operatorId, Guid actorUserId, Guid proposalId, CancellationToken cancellationToken)
    {
        var preflight = await proposals.GetOwnedByIdAsync(operatorId, proposalId, cancellationToken)
            ?? throw ProposalNotFound();
        var preflightTrip = await trips.GetRouteChangePreflightAsync(preflight.TripId, cancellationToken)
            ?? throw ProposalNotFound();
        EnsurePending(preflight);
        var preflightCustomDestinationId = preflight.Type == RouteChangeProposalType.CUSTOM
            ? preflight.DestinationStationId
            : (Guid?)null;
        var preflightCustomStopIds = preflight.Type == RouteChangeProposalType.CUSTOM
            ? preflight.Stops.Select(stop => stop.StopId).Distinct().OrderBy(id => id).ToArray()
            : [];
        var impact = await bookingImpact.GetTripEditImpactAsync(preflight.TripId, operatorId, cancellationToken);
        var affectedBookingIds = impact.ActiveBookings.Select(x => x.BookingId).Distinct().OrderBy(x => x).ToArray();

        var attempt = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            Station? lockedCustomStation = null;
            OperatorStation? lockedOperatorStation = null;
            IReadOnlyList<Stop> lockedCustomStops = [];
            if (preflight.Type == RouteChangeProposalType.EXISTING)
            {
                await proposals.AcquireSourceCoordinationLockAsync(
                    preflight.SourceAlternativeRouteId!.Value,
                    cancellationToken);
            }
            var trip = await trips.AcquireForRouteChangeAsync(preflight.TripId, cancellationToken) ?? throw ProposalNotFound();
            if (trip.OperatorId != operatorId)
                throw ProposalNotFound();
            var pending = await proposals.AcquirePendingByTripAsync(trip.Id, cancellationToken);
            var proposal = pending.SingleOrDefault(item => item.Id == proposalId && item.OperatorId == operatorId);
            if (proposal is null)
                throw new CodedConflictException("ROUTE_CHANGE_PROPOSAL_NOT_PENDING", "Route-change proposal is no longer pending.");
            await proposals.LoadStopsAsync(proposal, cancellationToken);
            var now = clock.UtcNow;
            if (!IsTripEditable(trip))
            {
                foreach (var item in pending)
                    await ExpireAsync(item, RouteChangeProposalResolutionCode.TripNoLongerEditable, now, cancellationToken);
                return (Response: (ApproveRouteChangeProposalResponse?)null, Stale: false);
            }

            if (proposal.Type == RouteChangeProposalType.CUSTOM)
            {
                lockedCustomStation = await stations.AcquireForRouteProposalApprovalAsync(
                    preflightCustomDestinationId!.Value,
                    cancellationToken);
                lockedOperatorStation = await operatorStations.AcquireActiveForRouteProposalApprovalAsync(
                    operatorId,
                    preflightCustomDestinationId.Value,
                    cancellationToken);
                lockedCustomStops = await stops.AcquireForRouteProposalApprovalAsync(
                    preflightCustomStopIds,
                    cancellationToken);
            }

            AlternativeRoute officialRoute;
            if (proposal.Type == RouteChangeProposalType.EXISTING)
            {
                var sourceRoute = await alternativeRoutes.AcquireOwnedByIdAsync(
                    operatorId,
                    proposal.SourceAlternativeRouteId!.Value,
                    cancellationToken);
                if (sourceRoute is null || !sourceRoute.IsActive || sourceRoute.RouteId != trip.RouteId || sourceRoute.UpdatedAt != proposal.SourceUpdatedAt)
                {
                    await ExpireAsync(proposal, RouteChangeProposalResolutionCode.SourceRouteChanged, now, cancellationToken);
                    return (Response: (ApproveRouteChangeProposalResponse?)null, Stale: true);
                }
                officialRoute = sourceRoute;
            }
            else
            {
                if (!IsLockedPromotedSnapshotCurrent(
                    proposal,
                    preflightCustomDestinationId,
                    preflightCustomStopIds,
                    lockedCustomStation,
                    lockedOperatorStation,
                    lockedCustomStops)
                    || !await IsGeometryValidForLockedSnapshotAsync(
                        proposal,
                        trip.RouteId,
                        lockedCustomStation,
                        lockedCustomStops,
                        cancellationToken))
                {
                    await ExpireAsync(proposal, RouteChangeProposalResolutionCode.SourceRouteChanged, now, cancellationToken);
                    return (Response: (ApproveRouteChangeProposalResponse?)null, Stale: true);
                }
                officialRoute = AlternativeRoute.Create(
                    trip.RouteId,
                    proposal.Name,
                    proposal.DestinationStationId,
                    proposal.TotalDistanceKm,
                    proposal.EstimatedDurationMinutes,
                    proposal.Description);
                officialRoute.SetPathGeometry(proposal.PathPolyline);
                var promotedStops = proposal.Stops
                    .Select(stop => AlternativeRouteStop.Create(officialRoute.Id, stop.StopId, stop.OrderIndex, stop.EstimatedDurationFromOriginMinutes, stop.DistanceFromOriginKm))
                    .ToArray();
                await alternativeRoutes.AddAsync(officialRoute, cancellationToken);
                await alternativeRoutes.ReplaceStopsAsync(officialRoute.Id, promotedStops, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            var routeChange = await tripRouteChanges.ApplyAsync(
                trip,
                officialRoute,
                affectedBookingIds,
                now,
                cancellationToken);
            proposal.Approve(actorUserId, officialRoute.Id, now);
            await AddAuditAsync(proposal, TripAuditAction.RouteChangeProposalApproved, actorUserId, now, cancellationToken);
            await AddTripRouteChangedAuditAsync(proposal, actorUserId, officialRoute.Id, now, cancellationToken);
            await EnqueueProposalEventAsync(RouteChangeProposalIntegrationEvent.Approved, proposal, actorUserId, now, cancellationToken);
            foreach (var other in pending.Where(item => item.Id != proposal.Id))
                await SupersedeAsync(other, actorUserId, proposal.Id, RouteChangeProposalResolutionCode.AnotherProposalApproved, now, cancellationToken);
            var change = new ChangeTripRouteResponse(
                routeChange.TripId,
                routeChange.Status,
                routeChange.AlternativeRouteId,
                routeChange.AffectedBookings);
            return (Response: new ApproveRouteChangeProposalResponse(RouteChangeProposalMapper.ToDto(proposal), change), Stale: false);
        }, cancellationToken);

        if (attempt.Stale)
            throw new CodedConflictException("ROUTE_CHANGE_PROPOSAL_STALE", "The source alternative route changed or was deactivated.");
        if (attempt.Response is null)
            throw new CodedConflictException("ROUTE_CHANGE_PROPOSAL_NOT_PENDING", "Trip is no longer editable and pending proposals were expired.");
        return attempt.Response;
    }

    public async Task<RouteChangeProposalDto> RejectAsync(Guid operatorId, Guid actorUserId, Guid proposalId, string? rejectionReason, CancellationToken cancellationToken)
    {
        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var preflight = await proposals.GetOwnedByIdAsync(operatorId, proposalId, cancellationToken) ?? throw ProposalNotFound();
            _ = await trips.AcquireForRouteChangeAsync(preflight.TripId, cancellationToken) ?? throw ProposalNotFound();
            var proposal = (await proposals.AcquirePendingByTripAsync(preflight.TripId, cancellationToken))
                .SingleOrDefault(item => item.Id == proposalId && item.OperatorId == operatorId)
                ?? throw new CodedConflictException("ROUTE_CHANGE_PROPOSAL_NOT_PENDING", "Route-change proposal is no longer pending.");
            await proposals.LoadStopsAsync(proposal, cancellationToken);
            var now = clock.UtcNow;
            try { proposal.Reject(actorUserId, now, rejectionReason); }
            catch (ArgumentException exception) { throw new ValidationException(exception.Message, [new ValidationError("rejectionReason", exception.Message)]); }
            await AddAuditAsync(proposal, TripAuditAction.RouteChangeProposalRejected, actorUserId, now, cancellationToken);
            await EnqueueProposalEventAsync(RouteChangeProposalIntegrationEvent.Rejected, proposal, actorUserId, now, cancellationToken);
            return RouteChangeProposalMapper.ToDto(proposal);
        }, cancellationToken);
    }

    public async Task SupersedePendingAsync(Guid tripId, Guid? actorUserId, Guid? approvedProposalId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pending = await proposals.AcquirePendingByTripAsync(tripId, cancellationToken);
        foreach (var proposal in pending.Where(x => x.Id != approvedProposalId))
            await SupersedeAsync(proposal, actorUserId, approvedProposalId, RouteChangeProposalResolutionCode.RouteChangedDirectly, now, cancellationToken);
    }

    public async Task ExpirePendingForSourceAsync(Guid sourceAlternativeRouteId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await proposals.AcquireSourceCoordinationLockAsync(sourceAlternativeRouteId, cancellationToken);
        var pending = await proposals.AcquirePendingBySourceAsync(sourceAlternativeRouteId, cancellationToken);
        foreach (var proposal in pending)
            await ExpireAsync(proposal, RouteChangeProposalResolutionCode.SourceRouteChanged, now, cancellationToken);
    }

    public async Task ExpirePendingForTripAsync(Guid tripId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pending = await proposals.AcquirePendingByTripAsync(tripId, cancellationToken);
        foreach (var proposal in pending)
            await ExpireAsync(proposal, RouteChangeProposalResolutionCode.TripNoLongerEditable, now, cancellationToken);
    }

    private async Task ExpireAsync(RouteChangeProposal proposal, string resolutionCode, DateTimeOffset now, CancellationToken cancellationToken)
    {
        proposal.Expire(resolutionCode, now);
        await AddAuditAsync(proposal, TripAuditAction.RouteChangeProposalExpired, null, now, cancellationToken);
        await EnqueueProposalEventAsync(RouteChangeProposalIntegrationEvent.Expired, proposal, null, now, cancellationToken);
    }

    private async Task SupersedeAsync(RouteChangeProposal proposal, Guid? actorUserId, Guid? winnerId, string resolutionCode, DateTimeOffset now, CancellationToken cancellationToken)
    {
        proposal.Supersede(actorUserId, winnerId, resolutionCode, now);
        await AddAuditAsync(proposal, TripAuditAction.RouteChangeProposalSuperseded, actorUserId, now, cancellationToken);
        await EnqueueProposalEventAsync(RouteChangeProposalIntegrationEvent.Superseded, proposal, actorUserId, now, cancellationToken);
    }

    private async Task<Domain.Entities.Trip> GetAssignedTripAsync(Guid tripId, Guid userId, CancellationToken cancellationToken)
    {
        var trip = await trips.GetByIdAsync(tripId, cancellationToken) ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
        if (trip.DriverUserId != userId && trip.AssistantUserId != userId)
            throw new ForbiddenException("FORBIDDEN", "Caller is not assigned to this trip.");
        return trip;
    }

    private async Task<Domain.Entities.Trip> GetAssignedTripForProposalCreationAsync(
        Guid tripId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var trip = await trips.AcquireForRouteChangeAsync(tripId, cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
        if (trip.DriverUserId != userId && trip.AssistantUserId != userId)
            throw new ForbiddenException("FORBIDDEN", "Caller is not assigned to this trip.");
        return trip;
    }

    private async Task ValidateIncidentAsync(Guid? incidentId, Guid tripId, CancellationToken cancellationToken)
    {
        if (!incidentId.HasValue) return;
        var incident = await incidents.GetByIdAsync(incidentId.Value, cancellationToken);
        if (incident is null || incident.TripId != tripId)
            throw new CodedNotFoundException("INCIDENT_NOT_FOUND", "Incident was not found for the proposed trip.");
    }

    private async Task ValidateSnapshotAsync(
        RouteChangeProposalSnapshotInput snapshot,
        Guid operatorId,
        Guid routeId,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = AlternativeRoute.Create(Guid.NewGuid(), snapshot.Name, snapshot.DestinationStationId, snapshot.TotalDistanceKm, snapshot.EstimatedDurationMinutes, snapshot.Description);
        }
        catch (ArgumentException exception)
        {
            throw InvalidSnapshot(exception.Message);
        }
        var station = await stations.AcquireForRouteProposalApprovalAsync(
            snapshot.DestinationStationId,
            cancellationToken);
        if (station is null || !station.IsActive || station.DeletedAt is not null) throw new CodedNotFoundException("STATION_NOT_FOUND", "Station was not found.");
        var operatorStation = await operatorStations.AcquireActiveForRouteProposalApprovalAsync(
            operatorId,
            snapshot.DestinationStationId,
            cancellationToken);
        if (operatorStation is null)
            throw new CodedNotFoundException("STATION_NOT_FOUND", "Station was not found.");
        var parentRoute = await routes.GetOwnedByIdAsync(operatorId, routeId, cancellationToken);
        if (parentRoute is null)
            throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Route was not found.");
        if (snapshot.Stops.GroupBy(x => x.StopId).Any(x => x.Count() > 1) || snapshot.Stops.GroupBy(x => x.OrderIndex).Any(x => x.Count() > 1))
            throw InvalidSnapshot("Custom route stops and order indexes must be unique.");
        var validatedStopIds = new List<Guid>(snapshot.Stops.Count);
        foreach (var item in snapshot.Stops)
        {
            try
            {
                _ = AlternativeRouteStop.Create(Guid.NewGuid(), item.StopId, item.OrderIndex, item.EstimatedDurationFromOriginMinutes, item.DistanceFromOriginKm);
            }
            catch (ArgumentException exception)
            {
                throw InvalidSnapshot(exception.Message);
            }
            validatedStopIds.Add(item.StopId);
        }

        var validatedStops = await stops.AcquireForRouteProposalApprovalAsync(
            validatedStopIds,
            cancellationToken);
        if (validatedStops.Count != validatedStopIds.Count
            || validatedStops.Any(stop =>
                stop.OperatorId != operatorId
                || !stop.IsActive
                || stop.DeletedAt is not null))
            throw new CodedNotFoundException("STOP_NOT_FOUND", "Stop was not found.");

        var originStation = await stations.GetByIdAsync(parentRoute.OriginStationId, cancellationToken);
        ValidateGeometry(snapshot.PathPolyline, station, originStation, validatedStops);
    }

    private static bool IsLockedPromotedSnapshotCurrent(
        RouteChangeProposal proposal,
        Guid? preflightDestinationStationId,
        IReadOnlyList<Guid> preflightStopIds,
        Station? lockedStation,
        OperatorStation? lockedOperatorStation,
        IReadOnlyList<Stop> lockedStops)
    {
        var lockedProposalStopIds = proposal.Stops.Select(stop => stop.StopId).Distinct().OrderBy(id => id).ToArray();
        return proposal.Type == RouteChangeProposalType.CUSTOM
            && proposal.DestinationStationId == preflightDestinationStationId
            && lockedProposalStopIds.SequenceEqual(preflightStopIds)
            && lockedStation is not null
            && lockedStation.Id == proposal.DestinationStationId
            && lockedStation.IsActive
            && lockedStation.DeletedAt is null
            && lockedOperatorStation is not null
            && lockedOperatorStation.OperatorId == proposal.OperatorId
            && lockedOperatorStation.StationId == proposal.DestinationStationId
            && lockedOperatorStation.IsActive
            && lockedStops.Count == preflightStopIds.Count
            && lockedStops.All(stop => stop.OperatorId == proposal.OperatorId && stop.IsActive && stop.DeletedAt is null);
    }

    private async Task<bool> IsGeometryValidForLockedSnapshotAsync(
        RouteChangeProposal proposal,
        Guid routeId,
        Station? destinationStation,
        IReadOnlyList<Stop> lockedStops,
        CancellationToken cancellationToken)
    {
        if (destinationStation is null)
            return false;
        var parentRoute = await routes.GetOwnedByIdAsync(proposal.OperatorId, routeId, cancellationToken);
        if (parentRoute is null)
            return false;
        var originStation = await stations.GetByIdAsync(parentRoute.OriginStationId, cancellationToken);
        try
        {
            ValidateGeometry(proposal.PathPolyline, destinationStation, originStation, lockedStops);
            return true;
        }
        catch (CodedValidationException)
        {
            return false;
        }
    }

    private static void ValidateGeometry(
        string? pathPolyline,
        Station destinationStation,
        Station? originStation,
        IReadOnlyCollection<Stop> routeStops)
    {
        if (string.IsNullOrWhiteSpace(pathPolyline))
            throw InvalidSnapshot("Custom route pathPolyline is required.");
        var polyline = RouteGeometryValidator.DecodeAndValidate(pathPolyline);
        var stationPoints = new[] { originStation, destinationStation }
            .Where(station => station is not null && station.Latitude.HasValue && station.Longitude.HasValue)
            .Select(station => (station!.Id, new GeoPoint((double)station.Latitude!.Value, (double)station.Longitude!.Value)));
        RouteGeometryValidator.ValidateWaypoints(
            polyline,
            routeStops.Select(stop => (stop.Id, new GeoPoint((double)stop.Latitude, (double)stop.Longitude))),
            stationPoints);
    }

    private async Task EnqueueProposalEventAsync(string eventType, RouteChangeProposal proposal, Guid? actorUserId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var evt = new RouteChangeProposalIntegrationEvent(
            eventType,
            proposal.Id,
            proposal.TripId,
            proposal.OperatorId,
            proposal.ProposedByUserId,
            actorUserId,
            proposal.Type.ToString(),
            proposal.Status.ToString(),
            proposal.SourceAlternativeRouteId,
            proposal.ApprovedAlternativeRouteId,
            proposal.IncidentId,
            proposal.Reason,
            proposal.RejectionReason,
            proposal.ResolutionCode,
            proposal.SupersededByProposalId,
            now);
        await outbox.EnqueueAsync(evt.EventId, evt.EventType, JsonSerializer.Serialize(evt, JsonOptions), cancellationToken);
    }

    private async Task AddAuditAsync(RouteChangeProposal proposal, string action, Guid? actorUserId, DateTimeOffset now, CancellationToken cancellationToken)
        => await auditLogs.AddAsync(TripAuditLog.Create(Guid.NewGuid(), proposal.TripId, actorUserId, action, JsonSerializer.Serialize(new { proposalId = proposal.Id, proposalType = proposal.Type.ToString(), status = proposal.Status.ToString() }, JsonOptions), now), cancellationToken);

    private async Task AddTripRouteChangedAuditAsync(RouteChangeProposal proposal, Guid actorUserId, Guid alternativeRouteId, DateTimeOffset now, CancellationToken cancellationToken)
        => await auditLogs.AddAsync(TripAuditLog.Create(Guid.NewGuid(), proposal.TripId, actorUserId, TripAuditAction.TripRouteChanged, JsonSerializer.Serialize(new { proposalId = proposal.Id, alternativeRouteId }, JsonOptions), now), cancellationToken);

    private static RouteChangeProposalStopSnapshot ToSnapshotStop(AlternativeRouteStop stop) => new(stop.StopId, stop.OrderIndex, stop.EstimatedDurationFromOriginMinutes, stop.DistanceFromOriginKm);
    private static RouteChangeProposalType ParseType(string value) => Enum.TryParse<RouteChangeProposalType>(value?.Trim(), true, out var parsed) ? parsed : throw InvalidSnapshot("Type must be EXISTING or CUSTOM.");
    private static void EnsureTripEditable(Domain.Entities.Trip trip) { try { trip.EnsureAlternativeRouteChangeAllowed(); } catch (InvalidOperationException exception) { throw new CodedConflictException("TRIP_NOT_EDITABLE", exception.Message); } }
    private static bool IsTripEditable(Domain.Entities.Trip trip) => trip.Status is TripStatus.SCHEDULED or TripStatus.BOARDING or TripStatus.IN_PROGRESS;
    private static void EnsurePending(RouteChangeProposal proposal) { if (proposal.Status != RouteChangeProposalStatus.PENDING) throw new CodedConflictException("ROUTE_CHANGE_PROPOSAL_NOT_PENDING", "Route-change proposal is no longer pending."); }
    private static CodedNotFoundException ProposalNotFound() => new("ROUTE_CHANGE_PROPOSAL_NOT_FOUND", "Route-change proposal was not found.");
    private static CodedValidationException InvalidSnapshot(string message) => new("VALIDATION_ERROR", message, [new ValidationError("snapshot", message)]);
}
