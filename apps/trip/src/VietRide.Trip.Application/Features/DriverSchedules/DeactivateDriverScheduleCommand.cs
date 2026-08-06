using MediatR;

namespace VietRide.Trip.Application.Features.DriverSchedules;

public sealed record DeactivateDriverScheduleCommand(Guid OperatorId, Guid DriverScheduleId)
    : IRequest<DriverScheduleDto>;
