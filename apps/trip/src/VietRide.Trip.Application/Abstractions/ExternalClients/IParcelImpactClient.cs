using System.Text.Json.Serialization;

namespace VietRide.Trip.Application.Abstractions.ExternalClients;

public interface IParcelImpactClient
{
    Task<TripParcelCancellationImpactProjection> GetTripCancellationImpactAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken cancellationToken);

    Task<ParcelStopDepartureClearanceProjection> GetStopDepartureClearanceAsync(
        Guid tripId,
        Guid stopId,
        Guid operatorId,
        CancellationToken cancellationToken);
}

public sealed record ParcelStopDepartureClearanceProjection(
    Guid TripId,
    Guid StopId,
    Guid OperatorId,
    string Status,
    IReadOnlyList<Guid> UnresolvedParcelIds,
    Guid? ApprovalRequestId,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt);

public sealed record TripParcelCancellationImpactProjection(
    Guid TripId,
    IReadOnlyList<TripParcelCancellationImpactProjection.AffectedParcel> AffectedParcels)
{
    public sealed record AffectedParcel(
        Guid ParcelId,
        string Status,
        [property: JsonPropertyName("refundAmountVnd")] long RefundAmount);
}
