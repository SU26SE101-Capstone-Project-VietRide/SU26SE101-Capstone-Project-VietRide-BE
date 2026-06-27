namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public interface ITripServiceClient
{
    Task<TripSnapshotOutcome> GetTripParcelSnapshotAsync(
        Guid tripId,
        CancellationToken cancellationToken = default);
}
