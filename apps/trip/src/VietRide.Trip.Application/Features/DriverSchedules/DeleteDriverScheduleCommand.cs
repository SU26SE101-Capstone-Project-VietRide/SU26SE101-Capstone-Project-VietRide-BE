using MediatR;

namespace VietRide.Trip.Application.Features.DriverSchedules;

public sealed record DeleteDriverScheduleCommand(Guid OperatorId, Guid DriverScheduleId)
    : IRequest<IReadOnlyDictionary<string, bool>>;
