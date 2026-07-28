using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.Deliver;

[SkipTransaction]
public sealed record DeliverParcelCommand(
    Guid ParcelId,
    Guid ActorUserId,
    Guid OperatorId,
    IReadOnlyCollection<string>? PhotoUrls) : IRequest<DeliverParcelResponse>;
