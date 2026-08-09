using System.Text.Json;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.DriverSchedules;

public static class DriverScheduleMapper
{
    public static DriverScheduleDto ToDto(DriverSchedule schedule)
    {
        return new DriverScheduleDto(
            schedule.Id,
            schedule.OperatorId,
            schedule.RouteId,
            schedule.VehicleId,
            schedule.DriverUserId,
            schedule.AssistantUserId,
            schedule.DayOfWeek.Deserialize<int[]>() ?? [],
            schedule.DepartureTime,
            schedule.ValidFrom,
            schedule.ValidUntil,
            schedule.IsActive,
            schedule.CreatedAt,
            schedule.UpdatedAt,
            schedule.BaseFare?.Amount);
    }
}
