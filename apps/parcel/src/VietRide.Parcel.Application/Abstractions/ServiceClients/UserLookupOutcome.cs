namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public enum UserLookupOutcomeKind
{
    Success,
    UserNotFound,
    Forbidden,
    TransportError,
}

public sealed record UserLookupOutcome(
    UserLookupOutcomeKind Kind,
    IdentityUserInfo? UserInfo,
    string? ErrorMessage);
