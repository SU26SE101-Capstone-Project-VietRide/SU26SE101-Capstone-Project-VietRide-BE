namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public enum RecipientUserLookupOutcomeKind
{
    Success,
    UserNotFound,
    TransportError,
}

public sealed record RecipientUserLookupOutcome(
    RecipientUserLookupOutcomeKind Kind,
    Guid? UserId,
    string? ErrorMessage)
{
    public static RecipientUserLookupOutcome Success(Guid userId)
        => new(RecipientUserLookupOutcomeKind.Success, userId, null);

    public static RecipientUserLookupOutcome NotFound()
        => new(RecipientUserLookupOutcomeKind.UserNotFound, null, null);

    public static RecipientUserLookupOutcome TransportFailure(string message)
        => new(RecipientUserLookupOutcomeKind.TransportError, null, message);
}
