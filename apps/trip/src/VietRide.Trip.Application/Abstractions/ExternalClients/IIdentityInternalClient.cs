namespace VietRide.Trip.Application.Abstractions.ExternalClients;

/// <summary>
/// Application-facing client for Identity internal logical-FK validation.
/// </summary>
public interface IIdentityInternalClient
{
    /// <summary>
    /// Validates that the operator exists and is eligible to perform Day-7 Trip writes.
    /// </summary>
    Task<OperatorWriteEligibilityValidation> ValidateOperatorCanWriteAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default);

    Task<OperatorWriteEligibilityValidation> ValidateOperatorSubscriptionCanWriteAsync(
        Guid operatorId,
        bool requireShuttleModule,
        CancellationToken cancellationToken = default)
        => ValidateOperatorCanWriteAsync(operatorId, cancellationToken);

    Task<IdentityUserLookupResult> GetUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, IdentityUserProfile>> GetUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, IdentityUserProfile>>(new Dictionary<Guid, IdentityUserProfile>());

    Task<IdentityOperatorLookupResult> GetOperatorAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(IdentityOperatorLookupResult.ValidationFailure("Identity operator lookup is not implemented."));

    Task<IdentityCrewSearchResult> SearchOperatorCrewAsync(
        Guid operatorId,
        string search,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(IdentityCrewSearchResult.Failure("Identity crew search is not implemented."));

}

public sealed record IdentityCrewProfile(Guid UserId, string DisplayName, string Role);

public sealed record IdentityCrewSearchResult(
    bool Succeeded,
    IReadOnlyList<IdentityCrewProfile> Users,
    string? Message)
{
    public static IdentityCrewSearchResult Success(IReadOnlyList<IdentityCrewProfile> users) =>
        new(true, users, null);

    public static IdentityCrewSearchResult Failure(string message) =>
        new(false, [], message);
}

/// <summary>
/// Result of validating an operator through Identity internal endpoints.
/// </summary>
public sealed record OperatorWriteEligibilityValidation(
    bool IsAllowed,
    int? FailureStatusCode,
    string? ErrorCode,
    string? Message)
{
    public static OperatorWriteEligibilityValidation Allowed() => new(true, null, null, null);

    public static OperatorWriteEligibilityValidation Forbidden(string message) => new(
        false,
        403,
        "FORBIDDEN",
        message);

    public static OperatorWriteEligibilityValidation ValidationFailure(string message) => new(
        false,
        422,
        "VALIDATION_ERROR",
        message);
}

public sealed record IdentityUserLookupResult(
    bool Found,
    int? FailureStatusCode,
    string? ErrorCode,
    string? Message,
    Guid? Id,
    string? DisplayName,
    string? AvatarUrl,
    string? Role,
    Guid? OperatorId,
    string? Status)
{
    public string? Phone { get; init; }
    public static IdentityUserLookupResult Success(Guid id, string? displayName, string? avatarUrl, string role, Guid? operatorId, string status)
    {
        return new IdentityUserLookupResult(true, null, null, null, id, displayName, avatarUrl, role, operatorId, status);
    }

    public static IdentityUserLookupResult Success(Guid id, string role, Guid? operatorId, string status)
        => Success(id, null, null, role, operatorId, status);

    public static IdentityUserLookupResult ValidationFailure(string message) => new(
        false,
        422,
        "VALIDATION_ERROR",
        message,
        null,
        null,
        null,
        null,
        null,
        null);

    public static IdentityUserLookupResult Forbidden(string message) => new(
        false,
        403,
        "FORBIDDEN",
        message,
        null,
        null,
        null,
        null,
        null,
        null);
}

public sealed record IdentityUserProfile(
    Guid Id,
    string DisplayName,
    string? AvatarUrl,
    string Role,
    Guid? OperatorId,
    string Status,
    string? Phone = null);

public sealed record IdentityOperatorLookupResult(
    bool Found,
    int? FailureStatusCode,
    string? ErrorCode,
    string? Message,
    Guid? Id,
    string? Name)
{
    public static IdentityOperatorLookupResult Success(Guid id, string name)
    {
        return new IdentityOperatorLookupResult(true, null, null, null, id, name);
    }

    public static IdentityOperatorLookupResult ValidationFailure(string message) => new(
        false,
        422,
        "VALIDATION_ERROR",
        message,
        null,
        null);

    public static IdentityOperatorLookupResult Forbidden(string message) => new(
        false,
        403,
        "FORBIDDEN",
        message,
        null,
        null);
}

public interface ISubscriptionQuotaClient
{
    Task<QuotaAllocationResult> ClaimQuotaAllocationAsync(Guid operatorId, string resource, Guid resourceId, string? periodKey, CancellationToken cancellationToken = default);
    Task ReleaseQuotaAllocationAsync(Guid operatorId, Guid allocationId, CancellationToken cancellationToken = default);
}

public sealed record QuotaAllocationResult(bool IsAllowed, Guid? AllocationId, int? FailureStatusCode, string? ErrorCode, string? Message)
{
    public static QuotaAllocationResult Allowed(Guid allocationId) => new(true, allocationId, null, null, null);
    public static QuotaAllocationResult Rejected(int statusCode, string errorCode, string message) => new(false, null, statusCode, errorCode, message);
}
