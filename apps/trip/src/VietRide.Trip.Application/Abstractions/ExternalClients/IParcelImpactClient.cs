namespace VietRide.Trip.Application.Abstractions.ExternalClients;

public interface IParcelImpactClient
{
    Task<TripParcelCancellationImpactProjection> GetTripCancellationImpactAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken cancellationToken);
}

public sealed record TripParcelCancellationImpactProjection(
    Guid TripId,
    IReadOnlyList<TripParcelCancellationImpactProjection.AffectedParcel> AffectedParcels)
{
    public sealed record AffectedParcel(Guid ParcelId, string Status, long RefundAmount);
}
