namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record CheckDriverScheduleAvailabilityRequest(
    Guid RouteId,
    Guid? VehicleId,
    Guid DriverUserId,
    Guid? AssistantUserId,
    IReadOnlyCollection<int> DayOfWeek,
    TimeOnly DepartureTime,
    DateOnly ValidFrom,
    DateOnly? ValidUntil);
