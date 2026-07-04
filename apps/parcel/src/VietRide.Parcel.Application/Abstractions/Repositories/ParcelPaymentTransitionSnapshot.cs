using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Abstractions.Repositories;

public sealed record ParcelPaymentTransitionSnapshot(
    Guid ParcelId,
    string ParcelCode,
    ParcelStatus Status,
    long DepositAmount,
    long AdditionalAmount,
    Guid OperatorId,
    Guid TripId,
    Guid? BookingId,
    Guid SenderUserId,
    ParcelSizeCategory SizeCategory,
    Guid? AdditionalPaymentId);
