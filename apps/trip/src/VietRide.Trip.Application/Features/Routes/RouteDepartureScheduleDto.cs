using VietRide.Shared.Kernel.Time;

namespace VietRide.Trip.Application.Features.Routes;

public sealed record RouteDepartureScheduleDto(
    Guid Id,
    IReadOnlyCollection<int> DayOfWeek,
    TimeOnly DepartureTime,
    DateOnly ValidFrom,
    DateOnly? ValidUntil,
    bool IsActive)
{
    public string TimeZone => BusinessTime.TimeZoneId;
}
