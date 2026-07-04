namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public enum RouteOwnershipOutcomeKind { Success, RouteNotFound, TransportError }

public sealed record RouteOwnershipOutcome(
    RouteOwnershipOutcomeKind Kind,
    string? ErrorMessage);
