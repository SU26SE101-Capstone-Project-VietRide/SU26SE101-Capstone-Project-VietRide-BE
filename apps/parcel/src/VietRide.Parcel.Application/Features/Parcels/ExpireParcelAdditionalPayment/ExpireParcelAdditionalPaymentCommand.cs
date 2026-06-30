using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.ExpireParcelAdditionalPayment;

[SkipTransaction]
public sealed record ExpireParcelAdditionalPaymentCommand() : IRequest<int>;
