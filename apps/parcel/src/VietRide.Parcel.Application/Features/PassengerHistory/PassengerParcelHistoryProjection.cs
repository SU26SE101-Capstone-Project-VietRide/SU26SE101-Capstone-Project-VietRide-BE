using VietRide.Parcel.Application.Features.History;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Features.PassengerHistory;

internal sealed record PassengerParcelHistoryProjection(
    SentParcelHistoryItemDto History,
    ParcelStatus Status,
    Guid? DepositPaymentId,
    Guid? BalancePaymentId,
    long DepositRemainingAmount,
    long BalanceRemainingAmount,
    DateTimeOffset? LatestCheckInAt,
    DateTimeOffset? FinalPaymentDeadline,
    Guid? DropoffStopId = null,
    Guid? DestinationStationId = null);
