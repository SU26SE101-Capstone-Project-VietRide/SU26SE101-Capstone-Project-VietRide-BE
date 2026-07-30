using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.RecoverTransferClaims;

[SkipTransaction]
public sealed record RecoverTransferClaimsCommand : IRequest<int>;
