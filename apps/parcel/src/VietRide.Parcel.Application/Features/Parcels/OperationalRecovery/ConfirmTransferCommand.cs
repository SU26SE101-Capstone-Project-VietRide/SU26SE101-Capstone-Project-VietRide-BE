using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;

[SkipTransaction]
public sealed record ConfirmTransferCommand(
    Guid ParcelId,
    string ParcelCode,
    Guid ConfirmedByUserId,
    Guid IdempotencyKey,
    Guid? OperatorId = null,
    string? Role = null,
    Guid? ExpectedTargetTripId = null,
    bool RequireCrewAuthorization = true) : IRequest<OperationalParcelResponse>;
