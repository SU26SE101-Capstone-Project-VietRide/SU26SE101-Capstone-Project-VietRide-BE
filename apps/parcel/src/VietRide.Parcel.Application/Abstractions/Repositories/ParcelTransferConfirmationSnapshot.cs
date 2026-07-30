using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Abstractions.Repositories;

public sealed record ParcelTransferConfirmationSnapshot(
    Guid ParcelId,
    string ParcelCode,
    Guid OperatorId,
    Guid SourceTripId,
    ParcelStatus Status,
    Guid? TargetTripId,
    DateTimeOffset? TransferRequestedAt,
    Guid? ClaimId,
    DateTimeOffset? ClaimedAt,
    Guid? ClaimedByUserId,
    DateTimeOffset? TransferConfirmedAt,
    Guid? TransferConfirmedByUserId,
    Guid SenderUserId);
