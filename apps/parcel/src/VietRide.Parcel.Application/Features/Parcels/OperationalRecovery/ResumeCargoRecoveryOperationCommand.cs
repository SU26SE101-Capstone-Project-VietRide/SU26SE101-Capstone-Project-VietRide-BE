using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;

[SkipTransaction]
public sealed record ResumeCargoRecoveryOperationCommand(Guid OperationId)
    : IRequest<OperationalParcelResponse>;
