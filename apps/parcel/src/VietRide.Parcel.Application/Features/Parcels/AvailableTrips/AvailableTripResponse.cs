namespace VietRide.Parcel.Application.Features.Parcels.AvailableTrips;

public sealed record AvailableTripResponse(
    Guid TripId,
    Guid RouteId,
    string OperatorName,
    DateTimeOffset DepartureDateTime,
    decimal AvailableCargoWeightKg,
    long PriceVnd);
