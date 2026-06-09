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
