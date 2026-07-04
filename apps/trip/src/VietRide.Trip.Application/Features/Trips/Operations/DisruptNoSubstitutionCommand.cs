using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Trip.Application.Features.Trips.Operations;

[SkipTransaction]
public sealed record DisruptNoSubstitutionCommand(
    Guid TripId,
    Guid OperatorId,
    Guid ActorUserId,
    string Reason) : IRequest<DisruptNoSubstitutionResponse>;
