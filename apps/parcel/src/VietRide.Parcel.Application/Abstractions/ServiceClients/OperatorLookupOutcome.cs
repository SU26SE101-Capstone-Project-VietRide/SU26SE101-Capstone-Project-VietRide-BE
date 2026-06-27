namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public enum OperatorLookupOutcomeKind
{
    Success,
    OperatorNotFound,
    Forbidden,
    TransportError,
}

public sealed record OperatorLookupOutcome(
    OperatorLookupOutcomeKind Kind,
    IdentityOperatorInfo? OperatorInfo,
    string? ErrorMessage);
