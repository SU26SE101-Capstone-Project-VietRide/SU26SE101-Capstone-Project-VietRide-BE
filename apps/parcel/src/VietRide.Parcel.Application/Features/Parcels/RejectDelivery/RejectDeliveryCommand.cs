using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.RejectDelivery;

public sealed record RejectDeliveryCommand(Guid DeliveryToken, string RejectionReason) : IRequest<RejectDeliveryResponse>;
