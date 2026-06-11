namespace VietRide.Trip.Application.Features.VehicleTypes;

public sealed record VehicleTypeDto(
    Guid Id,
    string Code,
    string DisplayName,
    int? EstimatedPassengerLuggageKgPerSeat,
    int? DefaultSeatCount,
    bool IsSystemDefined,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
