namespace VietRide.Parcel.Application.Abstractions.Repositories;

public sealed record TripCancellationParcelImpact(
    Guid ParcelId,
    string Status,
    long RefundAmount);
