using System.Text.Json;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Events;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Stations.MergeStations;

public sealed class MergeStationsCommandHandler : IRequestHandler<MergeStationsCommand, MergeStationsResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IStationRepository _stations;
    private readonly IOperatorStationRepository _operatorStations;
    private readonly IRouteRepository _routes;
    private readonly IAlternativeRouteRepository _alternativeRoutes;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IRouteChangeProposalLifecycleService? _routeChangeProposals;

    public MergeStationsCommandHandler(
        IStationRepository stations,
        IOperatorStationRepository operatorStations,
        IRouteRepository routes,
        IAlternativeRouteRepository alternativeRoutes,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IClock clock,
        IRouteChangeProposalLifecycleService? routeChangeProposals = null)
    {
        _stations = stations;
        _operatorStations = operatorStations;
        _routes = routes;
        _alternativeRoutes = alternativeRoutes;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _routeChangeProposals = routeChangeProposals;
    }

    public async Task<MergeStationsResponse> Handle(
        MergeStationsCommand request,
        CancellationToken cancellationToken)
    {
        var lockedStations = await _stations.GetForMergeAsync(
            request.PrimaryStationId,
            request.DuplicateStationId,
            cancellationToken);
        if (lockedStations.Count != 2)
            throw StationNotFound();

        var primary = lockedStations.SingleOrDefault(station => station.Id == request.PrimaryStationId)
            ?? throw StationNotFound();
        var duplicate = lockedStations.SingleOrDefault(station => station.Id == request.DuplicateStationId)
            ?? throw StationNotFound();
        EnsureMergePreconditions(primary, duplicate);

        if (await _routes.HasStationMergeConflictAsync(
            duplicate.Id,
            primary.Id,
            cancellationToken))
        {
            throw MergeConflict("The merge would make a Route origin equal its destination.");
        }

        var primaryBefore = StationEventSnapshot.FromStation(primary);
        var duplicateBefore = StationEventSnapshot.FromStation(duplicate);
        try
        {
            primary.MergeProfileFrom(duplicate);
            var operatorCounts = await _operatorStations.RelinkForStationMergeAsync(
                duplicate.Id,
                primary.Id,
                cancellationToken);
            var routeCounts = await _routes.RelinkForStationMergeAsync(
                duplicate.Id,
                primary.Id,
                cancellationToken);
            var changedAlternativeRouteIds = await _alternativeRoutes.ListIdsByDestinationAsync(
                duplicate.Id,
                cancellationToken);
            if (_routeChangeProposals is not null)
            {
                foreach (var alternativeRouteId in changedAlternativeRouteIds)
                    await _routeChangeProposals.ExpirePendingForSourceAsync(alternativeRouteId, _clock.UtcNow, cancellationToken);
            }
            var alternativeRouteCount = await _alternativeRoutes.RelinkDestinationForStationMergeAsync(
                duplicate.Id,
                primary.Id,
                cancellationToken);
            var shuttleTripCount = await _stations.RelinkShuttleTripsAsync(
                duplicate.Id,
                primary.Id,
                cancellationToken);
            var flattenedRedirectCount = await _stations.FlattenMergeRedirectsAsync(
                duplicate.Id,
                primary.Id,
                cancellationToken);
            duplicate.MarkMergedInto(primary.Id, _clock.UtcNow);

            var counts = new StationRelinkedCounts(
                operatorCounts.RelinkedCount,
                operatorCounts.CollapsedCount,
                routeCounts.OriginCount,
                routeCounts.DestinationCount,
                alternativeRouteCount,
                shuttleTripCount,
                flattenedRedirectCount);
            var integrationEvent = new StationMergedIntegrationEvent(
                request.ActorUserId,
                request.IpAddress,
                request.UserAgent,
                primary.Id,
                duplicate.Id,
                primaryBefore,
                duplicateBefore,
                StationEventSnapshot.FromStation(primary),
                counts,
                _clock.UtcNow);
            await _outbox.EnqueueAsync(
                integrationEvent.EventType,
                JsonSerializer.Serialize(integrationEvent, JsonOptions),
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new MergeStationsResponse(StationMapper.ToDto(primary), duplicate.Id, counts);
        }
        catch (CodedConflictException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw MergeConflict(exception.Message);
        }
    }

    private static void EnsureMergePreconditions(Station primary, Station duplicate)
    {
        if (!primary.IsActive || primary.DeletedAt.HasValue || primary.MergedIntoStationId.HasValue)
            throw MergeConflict("The primary Station must be active, non-deleted, and canonical.");
        if (duplicate.DeletedAt.HasValue || duplicate.MergedIntoStationId.HasValue)
            throw MergeConflict("The duplicate Station must be non-deleted and canonical.");
    }

    private static CodedNotFoundException StationNotFound()
        => new("STATION_NOT_FOUND", "One or more Stations were not found.");

    private static CodedConflictException MergeConflict(string message)
        => new("STATION_MERGE_CONFLICT", message);
}
