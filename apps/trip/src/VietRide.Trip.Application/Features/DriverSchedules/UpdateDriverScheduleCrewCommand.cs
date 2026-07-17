using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Trip.Application.Features.DriverSchedules;

[SkipTransaction]
public sealed record UpdateDriverScheduleCrewCommand(
    Guid OperatorId,
    Guid DriverScheduleId,
    Guid ActorUserId,
    string RequestId,
    Guid DriverUserId,
    Guid? AssistantUserId) : IRequest<DriverScheduleDto>;
