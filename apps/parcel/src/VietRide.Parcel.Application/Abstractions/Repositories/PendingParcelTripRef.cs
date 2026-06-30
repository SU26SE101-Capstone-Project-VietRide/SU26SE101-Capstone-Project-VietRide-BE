namespace VietRide.Parcel.Application.Abstractions.Repositories;

public sealed record PendingParcelTripRef(
    Guid ParcelId,
    Guid TripId,
    DateTimeOffset CreatedAt);
