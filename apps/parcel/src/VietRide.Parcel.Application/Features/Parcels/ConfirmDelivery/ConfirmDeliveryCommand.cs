using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.ConfirmDelivery;

public sealed record ConfirmDeliveryCommand(Guid DeliveryToken, string IpAddress) : IRequest<ConfirmDeliveryResponse>;
