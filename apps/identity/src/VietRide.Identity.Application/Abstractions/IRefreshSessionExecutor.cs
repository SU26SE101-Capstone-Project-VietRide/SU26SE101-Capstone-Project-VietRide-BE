using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Application.Abstractions;

public sealed record RefreshSessionResult(
    User? User,
    string? RefreshToken,
    OperatorRegistrationStatus? OperatorStatus,
    string? FailureCode,
    string? FailureMessage)
{
    public bool IsSuccess => User is not null && RefreshToken is not null;

    public static RefreshSessionResult Success(
        User user,
        string refreshToken,
        OperatorRegistrationStatus? operatorStatus = null)
        => new(user, refreshToken, operatorStatus, null, null);

    public static RefreshSessionResult Invalid(string message, string failureCode = "AUTH_TOKEN_INVALID")
        => new(null, null, null, failureCode, message);
}

/// <summary>
/// Owns the complete refresh-token mutation in a fresh transaction whose lock order is
/// User first, followed by the presented token and then its family.
/// </summary>
public interface IRefreshSessionExecutor
{
    Task<RefreshSessionResult> ExecuteAsync(string rawRefreshToken, CancellationToken ct = default);
}
