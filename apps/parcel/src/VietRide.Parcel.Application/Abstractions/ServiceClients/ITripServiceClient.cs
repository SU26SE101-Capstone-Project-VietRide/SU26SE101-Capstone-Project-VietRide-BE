using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public interface ITripServiceClient
{
    Task<TripSnapshotOutcome> GetTripParcelSnapshotAsync(
        Guid tripId,
        CancellationToken cancellationToken = default);

    Task<RouteOwnershipOutcome> ValidateRouteOwnershipAsync(
        Guid routeId,
        Guid operatorId,
        CancellationToken cancellationToken = default);

    Task<ParcelTripSearchOutcome> SearchAvailableParcelTripsAsync(
        Guid originStationId,
        Guid destinationStationId,
        DateOnly departureDate,
        decimal estimatedWeightKg,
        ParcelSizeCategory sizeCategory,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
