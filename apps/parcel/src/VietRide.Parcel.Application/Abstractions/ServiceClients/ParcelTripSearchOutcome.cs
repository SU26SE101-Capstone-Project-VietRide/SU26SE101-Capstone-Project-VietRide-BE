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
    Guid OperatorId,
    string OperatorName,
    DateTimeOffset DepartureDateTime,
    decimal AvailableCargoWeightKg,
    long PriceVnd);
