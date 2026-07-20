namespace VietRide.Trip.Application.Abstractions.ExternalClients;

public interface IBookingImpactClient
{
    Task<TripStopPendingPassengerCountProjection> GetPendingPassengerCountAsync(
        Guid tripId,
        Guid stopId,
        Guid operatorId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Pending-passenger count is not implemented by this client.");

    Task<TripBookingImpactProjection> GetTripEditImpactAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken cancellationToken);
}
