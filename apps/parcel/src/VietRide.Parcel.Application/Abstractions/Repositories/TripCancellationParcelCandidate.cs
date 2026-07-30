using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Abstractions.Repositories;

public sealed record TripCancellationParcelCandidate(
    Guid ParcelId,
    string ParcelCode,
    Guid OperatorId,
    Guid TripId,
    ParcelStatus Status,
    long DepositPaidVnd,
    long BalancePaidVnd,
    long RefundedAmountVnd,
    Guid SenderUserId,
    decimal EstimatedWeightKg,
    decimal EstimatedVolumeM3,
    decimal? ActualWeightKg,
    decimal? ActualVolumeM3);
