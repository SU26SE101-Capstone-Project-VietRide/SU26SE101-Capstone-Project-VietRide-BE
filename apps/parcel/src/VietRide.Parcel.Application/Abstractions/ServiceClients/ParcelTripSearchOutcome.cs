namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public enum ParcelTripSearchOutcomeKind { Success, TransportError }

public sealed record ParcelTripSearchOutcome(
    ParcelTripSearchOutcomeKind Kind,
    IReadOnlyList<ParcelTripDto>? Trips,
    int TotalItems,
    int Page,
    int PageSize,
    string? ErrorMessage);

public sealed record ParcelTripDto(
    Guid TripId,
    Guid RouteId,
    string Status,
    Guid OperatorId,
    string OperatorName,
    TripStationDto OriginStation,
    TripStationDto DestinationStation,
    DateTimeOffset DepartureDateTime,
    DateTimeOffset EstimatedArrivalTime,
    decimal AvailableCargoWeightKg,
    decimal AvailableCargoVolumeM3,
    long PriceVnd = 0);
