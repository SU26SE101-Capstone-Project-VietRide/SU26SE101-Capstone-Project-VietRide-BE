namespace VietRide.Trip.Application.Features.Stations;

public sealed record OperatorStationDto(Guid Id, Guid OperatorId, Guid StationId, StationDto Station,
    string? DisplayNameOverride, string? CounterLocation, string? ContactPhone, string? Instructions,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
