using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Trip.Application.Features.Trips.Operations;

[SkipTransaction]
public sealed record SubstituteVehicleCommand(
    Guid TripId,
    Guid OperatorId,
    Guid ActorUserId,
    Guid NewVehicleId,
    Guid NewDriverUserId,
    Guid? NewAssistantUserId,
    string Reason) : IRequest<SubstituteVehicleResponse>;
