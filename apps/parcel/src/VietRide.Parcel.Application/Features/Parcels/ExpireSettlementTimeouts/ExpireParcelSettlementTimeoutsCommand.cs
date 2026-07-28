using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.ExpireSettlementTimeouts;

[SkipTransaction]
public sealed record ExpireParcelSettlementTimeoutsCommand : IRequest<int>;
