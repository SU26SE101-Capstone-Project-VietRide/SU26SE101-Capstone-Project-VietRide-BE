using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.ManualConfirmDelivery;

public sealed record ManualConfirmDeliveryCommand(
    Guid ParcelId,
    Guid ActorUserId,
    Guid OperatorId,
    string Note,
    string ActorRole = "OPERATOR_STAFF") : IRequest<ManualConfirmDeliveryResponse>;
