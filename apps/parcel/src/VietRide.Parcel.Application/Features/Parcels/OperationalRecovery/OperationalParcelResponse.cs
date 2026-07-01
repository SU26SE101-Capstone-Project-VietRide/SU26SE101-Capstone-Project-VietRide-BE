namespace VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;

public sealed record OperationalParcelResponse(
    Guid ParcelId,
    string ParcelCode,
    string Status,
    Guid? TripId = null,
    Guid? TransferTargetTripId = null,
    DateTimeOffset? TransferConfirmedAt = null,
    string? ReturnReason = null,
    DateTimeOffset? ReturnedAt = null);
