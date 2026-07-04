using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.UndoRejectDelivery;

public sealed record UndoRejectDeliveryCommand(Guid DeliveryToken) : IRequest<UndoRejectDeliveryResponse>;
