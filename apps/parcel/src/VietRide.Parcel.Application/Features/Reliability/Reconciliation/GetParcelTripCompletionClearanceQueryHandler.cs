using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Services;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Reliability.Reconciliation;

public sealed class GetParcelTripCompletionClearanceQueryHandler
    : IRequestHandler<GetParcelTripCompletionClearanceQuery, ParcelTripCompletionClearanceResponse>
{
    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;

    public GetParcelTripCompletionClearanceQueryHandler(
        IParcelRepository parcels,
        IParcelReliabilityRepository reliability)
    {
        _parcels = parcels;
        _reliability = reliability;
    }

    public async Task<ParcelTripCompletionClearanceResponse> Handle(
        GetParcelTripCompletionClearanceQuery query,
        CancellationToken cancellationToken)
    {
        var manifest = await _parcels.ListTerminalDropoffManifestByTripAsync(
            query.TripId,
            cancellationToken);
        if (manifest.Any(parcel => parcel.OperatorId != query.OperatorId))
            throw new ForbiddenException("FORBIDDEN", "Trip Parcel manifest does not belong to this operator.");

        var incidents = await _reliability.ListActiveIncidentsByParcelsAsync(
            manifest.Select(parcel => parcel.Id).ToArray(),
            cancellationToken);
        var decision = ParcelTripCompletionClearancePolicy.Evaluate(manifest, incidents);

        return new ParcelTripCompletionClearanceResponse(
            query.TripId,
            query.OperatorId,
            decision.Status,
            decision.UnresolvedParcelIds,
            decision.IncidentIds);
    }
}
