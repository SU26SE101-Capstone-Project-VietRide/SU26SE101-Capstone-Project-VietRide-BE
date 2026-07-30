namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record IdentityUserBatchOutcome(
    IdentityUserBatchOutcomeKind Kind,
    IReadOnlyList<IdentityUserSummary> Users,
    string? ErrorMessage)
{
    public static IdentityUserBatchOutcome Success(IReadOnlyList<IdentityUserSummary> users)
        => new(IdentityUserBatchOutcomeKind.Success, users, null);

    public static IdentityUserBatchOutcome TransportFailure(string errorMessage)
        => new(IdentityUserBatchOutcomeKind.TransportError, [], errorMessage);
}
