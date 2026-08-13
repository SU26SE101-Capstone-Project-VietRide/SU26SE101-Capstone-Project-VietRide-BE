namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public interface IIdentityServiceClient
{
    Task<UserLookupOutcome> GetUserInfoAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<RecipientUserLookupOutcome> FindUserByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
        => Task.FromResult(RecipientUserLookupOutcome.NotFound());

    Task<IdentityUserBatchOutcome> GetUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Identity user batch lookup is not implemented by this client.");

    Task<OperatorLookupOutcome> GetOperatorInfoAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default);

    Task<SubscriptionWriteEligibilityOutcome> GetSubscriptionWriteEligibilityAsync(
        Guid operatorId,
        bool requireParcelModule,
        CancellationToken cancellationToken = default)
        => Task.FromResult(SubscriptionWriteEligibilityOutcome.Allowed());
}

public sealed record SubscriptionWriteEligibilityOutcome(
    bool IsAllowed,
    int? FailureStatusCode,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static SubscriptionWriteEligibilityOutcome Allowed() => new(true, null, null, null);
    public static SubscriptionWriteEligibilityOutcome Rejected(int statusCode, string errorCode, string message)
        => new(false, statusCode, errorCode, message);
}
