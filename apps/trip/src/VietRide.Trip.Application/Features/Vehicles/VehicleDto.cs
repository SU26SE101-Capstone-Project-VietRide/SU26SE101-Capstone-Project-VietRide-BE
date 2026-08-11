namespace VietRide.Trip.Application.Features.Vehicles;

public sealed record VehicleDto(
    Guid Id,
    Guid OperatorId,
    Guid VehicleTypeId,
    string LicensePlate,
    SeatLayoutDto SeatLayoutJson,
    int TotalSeats,
    int UsablePassengerCapacity,
    decimal? MaxCargoWeightKg,
    decimal? MaxCargoVolumeM3,
    IReadOnlyCollection<string>? ImageUrls,
    VehicleStatusDto Status,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    VehicleAssignmentDto? CurrentAssignment = null,
    VehicleAssignmentDto? NextAssignment = null);

public sealed record VehicleAssignmentDto(
    string SourceType,
    Guid? TripId,
    Guid? ShuttleTripId,
    Guid DriverUserId,
    DateTimeOffset PlannedStartAt,
    DateTimeOffset PlannedEndAt,
    string Status,
    Guid? StartStationId,
    Guid? EndStationId);
