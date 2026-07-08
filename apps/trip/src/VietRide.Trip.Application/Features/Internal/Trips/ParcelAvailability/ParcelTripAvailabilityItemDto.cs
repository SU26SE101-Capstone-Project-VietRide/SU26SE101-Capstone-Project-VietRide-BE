namespace VietRide.Trip.Application.Features.Internal.Trips.ParcelAvailability;

public sealed record ParcelTripAvailabilityItemDto(
    Guid TripId,
    Guid RouteId,
    Guid OperatorId,
    string OperatorName,
    DateTimeOffset DepartureDateTime,
    decimal AvailableCargoWeightKg);
