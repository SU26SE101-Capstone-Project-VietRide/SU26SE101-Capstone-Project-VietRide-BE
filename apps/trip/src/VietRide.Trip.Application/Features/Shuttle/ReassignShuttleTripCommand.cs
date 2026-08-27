using MediatR;
using VietRide.Shared.Application.Behaviors;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

[SkipTransaction]
public sealed record ReassignShuttleTripCommand(
    Guid OperatorId,
    Guid ActorUserId,
    Guid ShuttleTripId,
    Guid? DriverUserId,
    Guid? VehicleId,
    string? Reason) : IRequest<ReassignShuttleTripResult>;
