namespace VietRide.Trip.Application.Features.DriverSchedules.GetMyDriverSchedule;

public sealed record GetMyDriverScheduleDto(
    Guid TripId,
    Guid OperatorId,
    Guid RouteId,
    Guid VehicleId,
    DateTimeOffset DepartureDateTime,
    DateTimeOffset EstimatedArrivalTime,
    string Status,
    string AssignmentRole);

public sealed record GetMyDriverScheduleResult(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<GetMyDriverScheduleDto> Trips);
