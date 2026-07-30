namespace VietRide.Parcel.Application.Abstractions.Repositories;

public sealed record ParcelDeliveryReminderSnapshot(
    Guid ParcelId,
    string ParcelCode,
    Guid OperatorId,
    Guid TripId,
    DateTimeOffset ExpiredAt);
