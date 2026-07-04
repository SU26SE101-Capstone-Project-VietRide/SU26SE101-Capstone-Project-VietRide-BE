namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record BookingSnapshot(
    Guid BookingId,
    Guid UserId,
    Guid TripId,
    string Status);
