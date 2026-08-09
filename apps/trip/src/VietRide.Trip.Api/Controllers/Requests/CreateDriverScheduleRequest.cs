namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record CreateDriverScheduleRequest(
    Guid RouteId,
    Guid? VehicleId,
    Guid DriverUserId,
    Guid? AssistantUserId,
    IReadOnlyCollection<int> DayOfWeek,
    TimeOnly DepartureTime,
    DateOnly ValidFrom,
    DateOnly? ValidUntil,
    bool IsActive,
    long? BaseFare = null);
