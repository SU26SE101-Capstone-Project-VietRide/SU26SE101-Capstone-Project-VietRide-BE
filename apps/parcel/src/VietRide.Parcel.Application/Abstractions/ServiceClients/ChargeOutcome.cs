namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public enum ChargeOutcomeKind
{
    Success,
    InsufficientFunds,
    TransportError,
}

public sealed record ChargeOutcome(
    ChargeOutcomeKind Kind,
    ChargeResult? Result,
    string? ErrorMessage);
