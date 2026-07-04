using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;

[SkipTransaction]
public sealed record ConfirmTransferCommand(
    Guid ParcelId,
    Guid TargetTripId,
    string ParcelCode,
    Guid ConfirmedByUserId) : IRequest<OperationalParcelResponse>;
