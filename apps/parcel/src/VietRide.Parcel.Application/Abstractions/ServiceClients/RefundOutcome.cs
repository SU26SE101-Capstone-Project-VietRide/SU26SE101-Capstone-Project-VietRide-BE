namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public enum RefundOutcomeKind
{
    Success,
    TransportError,
}

public sealed record RefundOutcome(
    RefundOutcomeKind Kind,
    RefundResult? Result,
    string? ErrorMessage);
