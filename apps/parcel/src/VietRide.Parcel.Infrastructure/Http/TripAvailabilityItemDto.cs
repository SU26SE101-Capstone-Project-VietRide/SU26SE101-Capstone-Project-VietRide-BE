namespace VietRide.Parcel.Infrastructure.Http;

/// Wire DTO matching the Trip service /internal/v1/trips/parcel-availability
/// response item. Exists so deserialization is explicit — NOT the application-level
/// outcome type (ParcelTripSearchOutcome).
public sealed record TripAvailabilityItemDto(
    Guid TripId,
    Guid RouteId,
    Guid OperatorId,
    string OperatorName,
    DateTimeOffset DepartureDateTime,
    decimal AvailableCargoWeightKg,
    decimal AvailableCargoVolumeM3);
