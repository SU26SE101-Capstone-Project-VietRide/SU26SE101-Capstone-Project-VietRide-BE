using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;

public sealed record ConfirmTransferCommand(
    Guid ParcelId,
    Guid TargetTripId,
    string ParcelCode,
    Guid ConfirmedByUserId) : IRequest<OperationalParcelResponse>;
