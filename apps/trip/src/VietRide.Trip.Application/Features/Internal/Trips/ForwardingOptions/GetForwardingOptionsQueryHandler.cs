using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Internal.Trips.ForwardingOptions;

public sealed class GetForwardingOptionsQueryHandler
    : IRequestHandler<GetForwardingOptionsQuery, IReadOnlyList<InternalForwardingOptionDto>>
{
    private readonly ITripRepository _trips;

    public GetForwardingOptionsQueryHandler(ITripRepository trips)
    {
        _trips = trips;
    }

    public async Task<IReadOnlyList<InternalForwardingOptionDto>> Handle(
        GetForwardingOptionsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.OperatorId == Guid.Empty
            || request.PickupLocationId == Guid.Empty
            || request.TargetLocationId == Guid.Empty
            || request.WeightKg <= 0
            || request.VolumeM3 <= 0
            || request.Limit is < 1 or > 50)
            throw new CodedValidationException("VALIDATION_ERROR", "Forwarding option criteria are invalid.");
        var pickupType = request.PickupLocationType.Trim().ToUpperInvariant();
        var targetType = request.TargetLocationType.Trim().ToUpperInvariant();
        if (pickupType is not ("ROUTE_STOP" or "ORIGIN_STATION" or "DESTINATION_STATION" or "WAREHOUSE")
            || targetType is not ("ROUTE_STOP" or "DESTINATION_STATION"))
            throw new CodedValidationException("VALIDATION_ERROR", "Forwarding locations are invalid.");

        var candidates = await _trips.ListForwardingCandidatesAsync(
            request.OperatorId,
            request.ExcludedTripId,
            pickupType,
            request.PickupLocationId,
            targetType,
            request.TargetLocationId,
            request.WeightKg,
            request.VolumeM3,
            request.EarliestDeparture,
            request.Limit,
            cancellationToken);
        var summaries = await _trips.ListSummariesByIdsAsync(
            candidates.Select(candidate => candidate.TripId).ToArray(),
            cancellationToken);
        var summaryById = summaries.ToDictionary(summary => summary.TripId);
        return candidates.Where(candidate => summaryById.TryGetValue(candidate.TripId, out var summary)
                && summary.AssistantUserId.HasValue)
            .Select(candidate => new InternalForwardingOptionDto(
                summaryById[candidate.TripId],
                request.PickupLocationId,
                pickupType,
                candidate.PickupName,
                request.TargetLocationId,
                targetType,
                candidate.TargetDropoffName,
                candidate.PickupAt,
                candidate.Eta,
                candidate.HasCargoCapacity,
                candidate.HasCargoCapacity ? null : "INSUFFICIENT_CARGO_CAPACITY"))
            .ToArray();
    }
}
