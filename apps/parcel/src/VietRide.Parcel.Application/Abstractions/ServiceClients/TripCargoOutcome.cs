namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public enum TripCargoOutcomeKind
{
    Success,
    TripNotFound,
    CapacityExceeded,
    TransportError,
}

public sealed record TripCargoOutcome(
    TripCargoOutcomeKind Kind,
    string? ErrorMessage);
