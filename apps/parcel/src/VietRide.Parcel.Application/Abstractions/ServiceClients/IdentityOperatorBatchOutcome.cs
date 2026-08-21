namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record IdentityOperatorBatchOutcome(
    IdentityOperatorBatchOutcomeKind Kind,
    IReadOnlyList<IdentityOperatorSummary> Operators,
    string? ErrorMessage)
{
    public static IdentityOperatorBatchOutcome Success(IReadOnlyList<IdentityOperatorSummary> operators)
        => new(IdentityOperatorBatchOutcomeKind.Success, operators, null);

    public static IdentityOperatorBatchOutcome TransportFailure(string errorMessage)
        => new(IdentityOperatorBatchOutcomeKind.TransportError, [], errorMessage);
}
