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

    Task<IdentityUserLookupResult> GetUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IdentityOperatorLookupResult> GetOperatorAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(IdentityOperatorLookupResult.ValidationFailure("Identity operator lookup is not implemented."));
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
    string? Role,
    Guid? OperatorId,
    string? Status)
{
    public static IdentityUserLookupResult Success(Guid id, string role, Guid? operatorId, string status)
    {
        return new IdentityUserLookupResult(true, null, null, null, id, role, operatorId, status);
    }

    public static IdentityUserLookupResult ValidationFailure(string message) => new(
        false,
        422,
        "VALIDATION_ERROR",
        message,
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
        null);
}

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
