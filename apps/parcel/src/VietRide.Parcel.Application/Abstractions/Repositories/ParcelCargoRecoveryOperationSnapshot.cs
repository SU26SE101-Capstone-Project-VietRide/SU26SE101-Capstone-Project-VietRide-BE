using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Abstractions.Repositories;

public sealed record ParcelCargoRecoveryOperationSnapshot(
    Guid Id,
    Guid ParcelId,
    string ParcelCode,
    Guid OperatorId,
    Guid SenderUserId,
    ParcelCargoRecoveryOperationType OperationType,
    ParcelCargoRecoveryOperationStatus OperationStatus,
    Guid SourceTripId,
    Guid? TargetTripId,
    string? TargetState,
    Guid ActorUserId,
    string Reason,
    long RefundAmountVnd,
    long RefundDueVnd,
    ParcelStatus SourceStatus,
    bool IsStatusOverride,
    DateTimeOffset ClaimedAt,
    DateTimeOffset? CompletedAt,
    string? FailureCode,
    decimal WeightKg,
    decimal VolumeM3,
    ParcelStatus ParcelStatus,
    Guid ParcelTripId,
    DateTimeOffset? ReturnedAt);
