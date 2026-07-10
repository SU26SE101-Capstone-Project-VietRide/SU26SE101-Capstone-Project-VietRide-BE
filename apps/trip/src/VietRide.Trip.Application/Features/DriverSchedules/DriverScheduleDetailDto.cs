using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Features.Routes;
using VietRide.Trip.Application.Features.Vehicles;

namespace VietRide.Trip.Application.Features.DriverSchedules;

public sealed record DriverScheduleDetailDto(
    Guid Id, Guid OperatorId, Guid RouteId, Guid? VehicleId, Guid DriverUserId, Guid? AssistantUserId,
    IReadOnlyCollection<int> DayOfWeek, TimeOnly DepartureTime, DateOnly ValidFrom, DateOnly? ValidUntil,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    RouteDto? Route, VehicleDto? Vehicle, IdentityUserProfile? Driver, IdentityUserProfile? Assistant);
