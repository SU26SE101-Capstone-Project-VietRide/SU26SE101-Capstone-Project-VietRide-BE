using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.ResendDeliveryEmail;

[SkipTransaction]
public sealed record ResendDeliveryEmailCommand(
    Guid ParcelId,
    Guid ActorUserId,
    Guid OperatorId,
    string ActorRole) : IRequest<ResendDeliveryEmailResponse>;
