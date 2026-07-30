using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Abstractions.Repositories;

public sealed record ParcelManualConfirmationSnapshot(
    Guid ParcelId,
    ParcelStatus Status,
    DateTimeOffset? ConfirmedAt,
    Guid? ConfirmedByUserId,
    string? ConfirmNote);
