using MediatR;

namespace VietRide.Trip.Application.Features.DriverSchedules;

public sealed record UpdateDriverScheduleCrewCommand(
    Guid OperatorId,
    Guid DriverScheduleId,
    Guid DriverUserId,
    Guid? AssistantUserId) : IRequest<DriverScheduleDto>;
