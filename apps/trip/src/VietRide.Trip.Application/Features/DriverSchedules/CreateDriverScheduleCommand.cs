using MediatR;

namespace VietRide.Trip.Application.Features.DriverSchedules;

public sealed record CreateDriverScheduleCommand(
    Guid OperatorId,
    Guid RouteId,
    Guid? VehicleId,
    Guid DriverUserId,
    Guid? AssistantUserId,
    IReadOnlyCollection<int> DayOfWeek,
    TimeOnly DepartureTime,
    DateOnly ValidFrom,
    DateOnly? ValidUntil) : IRequest<DriverScheduleDto>;
