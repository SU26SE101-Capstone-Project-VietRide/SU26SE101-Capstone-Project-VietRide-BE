namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public enum TripOperationalLocationOutcomeKind
{
    Success,
    TripNotFound,
    TransportError,
}

public sealed record TripOperationalLocationOutcome(
    TripOperationalLocationOutcomeKind Kind,
    TripOperationalLocationSnapshot? Snapshot,
    string? ErrorMessage);
