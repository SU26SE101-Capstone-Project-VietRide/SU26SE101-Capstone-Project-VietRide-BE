namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public enum TripSnapshotOutcomeKind
{
    Success,
    TripNotFound,
    TransportError,
}

public sealed record TripSnapshotOutcome(
    TripSnapshotOutcomeKind Kind,
    TripParcelSnapshot? Snapshot,
    string? ErrorMessage);
