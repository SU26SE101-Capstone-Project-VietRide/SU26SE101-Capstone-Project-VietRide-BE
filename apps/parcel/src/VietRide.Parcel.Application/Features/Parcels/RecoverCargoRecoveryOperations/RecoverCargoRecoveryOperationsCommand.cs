using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.RecoverCargoRecoveryOperations;

[SkipTransaction]
public sealed record RecoverCargoRecoveryOperationsCommand : IRequest<int>;
