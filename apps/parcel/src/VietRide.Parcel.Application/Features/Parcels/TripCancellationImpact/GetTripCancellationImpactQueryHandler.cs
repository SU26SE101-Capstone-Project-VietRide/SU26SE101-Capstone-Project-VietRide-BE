using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;

namespace VietRide.Parcel.Application.Features.Parcels.TripCancellationImpact;

public sealed class GetTripCancellationImpactQueryHandler(IParcelRepository parcels)
    : IRequestHandler<GetTripCancellationImpactQuery, TripCancellationImpactResponse>
{
    public async Task<TripCancellationImpactResponse> Handle(
        GetTripCancellationImpactQuery request,
        CancellationToken cancellationToken)
    {
        var impacts = await parcels.GetTripCancellationImpactAsync(
            request.TripId,
            request.OperatorId,
            cancellationToken);
        return new TripCancellationImpactResponse(
            request.TripId,
            impacts.Select(impact => new TripCancellationImpactResponse.AffectedParcel(
                impact.ParcelId,
                impact.Status,
                impact.RefundAmount)).ToArray());
    }
}
