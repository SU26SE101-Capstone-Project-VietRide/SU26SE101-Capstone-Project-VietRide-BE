using MediatR;

namespace VietRide.Trip.Application.Features.DriverSchedules;

public sealed record ActivateDriverScheduleCommand(Guid OperatorId, Guid DriverScheduleId) : IRequest<DriverScheduleDto>;
