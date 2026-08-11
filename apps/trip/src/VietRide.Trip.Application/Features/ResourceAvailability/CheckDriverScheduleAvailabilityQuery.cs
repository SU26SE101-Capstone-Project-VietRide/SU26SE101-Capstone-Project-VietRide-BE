using VietRide.Shared.Application.Cqrs;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.ResourceAvailability;

public sealed record CheckDriverScheduleAvailabilityQuery(
    Guid OperatorId,
    Guid RouteId,
    Guid? VehicleId,
    Guid DriverUserId,
    Guid? AssistantUserId,
    IReadOnlyCollection<int> DayOfWeek,
    TimeOnly DepartureTime,
    DateOnly ValidFrom,
    DateOnly? ValidUntil) : IQuery<ResourceAvailabilityResult>;
