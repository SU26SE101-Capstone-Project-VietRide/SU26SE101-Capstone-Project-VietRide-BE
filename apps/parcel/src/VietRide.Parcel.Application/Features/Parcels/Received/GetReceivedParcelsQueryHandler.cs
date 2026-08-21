using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Abstractions.Services;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.Parcels.Received;

public sealed class GetReceivedParcelsQueryHandler
    : IRequestHandler<GetReceivedParcelsQuery, PagedResult<ReceivedParcelResponse>>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly ITripServiceClient _tripClient;
    private readonly IParcelReliabilityReadModelService? _screenModels;

    public GetReceivedParcelsQueryHandler(
        IParcelRepository parcelRepository,
        ITripServiceClient tripClient,
        IParcelReliabilityReadModelService? screenModels = null)
    {
        _parcelRepository = parcelRepository;
        _tripClient = tripClient;
        _screenModels = screenModels;
    }

    public async Task<PagedResult<ReceivedParcelResponse>> Handle(
        GetReceivedParcelsQuery query,
        CancellationToken cancellationToken)
    {
        var pagedResult = await _parcelRepository.ListReceivedByUserIdAsync(
            query.UserId, query.Page, query.PageSize, cancellationToken);

        if (_screenModels is not null)
        {
            var screens = await _screenModels.BuildAsync(
                pagedResult.Items,
                query.UserId,
                includeClaim: false,
                cancellationToken);
            var enrichedItems = pagedResult.Items.Select(parcel =>
            {
                screens.TryGetValue(parcel.Id, out var screen);
                return new ReceivedParcelResponse(
                    parcel.Id,
                    parcel.ParcelCode,
                    parcel.Status.ToString(),
                    screen?.Trip.Route?.Origin is { } origin
                        ? new ReceivedParcelStationResponse(origin.Id ?? Guid.Empty, origin.Name ?? string.Empty)
                        : null,
                    screen?.Trip.Route?.Destination is { } destination
                        ? new ReceivedParcelStationResponse(destination.Id ?? Guid.Empty, destination.Name ?? string.Empty)
                        : null,
                    screen?.Trip.Eta,
                    parcel.SenderUserId,
                    parcel.RecipientName,
                    parcel.SizeCategory.ToString(),
                    parcel.CreatedAt,
                    parcel.OperatorId,
                    parcel.TripId,
                    screen?.Operator,
                    screen?.DropoffLocation,
                    screen?.Reliability);
            }).ToArray();
            return PagedResult<ReceivedParcelResponse>.Create(
                enrichedItems,
                pagedResult.Page,
                pagedResult.PageSize,
                pagedResult.TotalItems);
        }

        var distinctTripIds = pagedResult.Items
            .Select(p => p.TripId)
            .Distinct()
            .ToList();

        var tripSnapshots = new Dictionary<Guid, TripParcelSnapshot>();
        foreach (var tripId in distinctTripIds)
        {
            var outcome = await _tripClient.GetTripParcelSnapshotAsync(tripId, cancellationToken);
            switch (outcome.Kind)
            {
                case TripSnapshotOutcomeKind.TripNotFound:
                case TripSnapshotOutcomeKind.TransportError:
                    continue;
                default:
                    tripSnapshots[tripId] = outcome.Snapshot!;
                    break;
            }
        }

        var items = pagedResult.Items.Select(parcel =>
        {
            tripSnapshots.TryGetValue(parcel.TripId, out var trip);
            return new ReceivedParcelResponse(
                parcel.Id,
                parcel.ParcelCode,
                parcel.Status.ToString(),
                trip is null ? null : new ReceivedParcelStationResponse(trip.OriginStation.Id, trip.OriginStation.Name),
                trip is null ? null : new ReceivedParcelStationResponse(trip.DestinationStation.Id, trip.DestinationStation.Name),
                trip?.EstimatedArrivalTime,
                parcel.SenderUserId,
                parcel.RecipientName,
                parcel.SizeCategory.ToString(),
                parcel.CreatedAt,
                parcel.OperatorId,
                parcel.TripId);
        }).ToList();

        return PagedResult<ReceivedParcelResponse>.Create(
            items,
            pagedResult.Page,
            pagedResult.PageSize,
            pagedResult.TotalItems);
    }
}
