using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Abstractions.Repositories;

public sealed record ParcelEventSnapshot(
    Guid ParcelId,
    string ParcelCode,
    Guid OperatorId,
    Guid TripId,
    ParcelStatus Status,
    long DepositAmount = 0,
    long AdditionalAmount = 0,
    Guid SenderUserId = default,
    Guid? RecipientUserId = null,
    Guid? DeliveryToken = null,
    DateTimeOffset? DeliveryTokenExpiresAt = null);
