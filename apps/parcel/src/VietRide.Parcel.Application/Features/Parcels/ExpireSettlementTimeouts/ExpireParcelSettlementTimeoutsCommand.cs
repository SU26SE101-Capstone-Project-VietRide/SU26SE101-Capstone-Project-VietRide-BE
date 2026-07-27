using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.ExpireSettlementTimeouts;

public sealed record ExpireParcelSettlementTimeoutsCommand : IRequest<int>;
