namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public enum BookingLookupOutcomeKind
{
    Success,
    BookingNotFound,
    TransportError,
}

public sealed record BookingLookupOutcome(
    BookingLookupOutcomeKind Kind,
    BookingSnapshot? Snapshot,
    string? ErrorMessage);
