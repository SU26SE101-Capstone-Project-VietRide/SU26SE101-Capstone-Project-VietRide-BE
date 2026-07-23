using VietRide.Shared.Application.Cqrs;

namespace VietRide.Parcel.Application.Features.Parcels.TripCancellationImpact;

public sealed record GetTripCancellationImpactQuery(Guid TripId, Guid OperatorId)
    : IQuery<TripCancellationImpactResponse>;

public sealed record TripCancellationImpactResponse(
    Guid TripId,
    IReadOnlyList<TripCancellationImpactResponse.AffectedParcel> AffectedParcels)
{
    public sealed record AffectedParcel(Guid ParcelId, string Status, long RefundAmount);
}
