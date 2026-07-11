namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public enum TripCrewAuthorizationOutcomeKind
{
    Authorized,
    Denied,
    TripNotFound,
    TransportError,
}

public sealed record TripCrewAuthorizationOutcome(
    TripCrewAuthorizationOutcomeKind Kind,
    string? ErrorMessage = null);
