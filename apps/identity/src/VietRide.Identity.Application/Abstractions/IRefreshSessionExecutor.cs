using VietRide.Identity.Domain.Entities;

namespace VietRide.Identity.Application.Abstractions;

public sealed record RefreshSessionResult(
    User? User,
    string? RefreshToken,
    string? FailureMessage)
{
    public bool IsSuccess => User is not null && RefreshToken is not null;

    public static RefreshSessionResult Success(User user, string refreshToken)
        => new(user, refreshToken, null);

    public static RefreshSessionResult Invalid(string message)
        => new(null, null, message);
}

/// <summary>
/// Owns the complete refresh-token mutation in a fresh transaction whose lock order is
/// User first, followed by the presented token and then its family.
/// </summary>
public interface IRefreshSessionExecutor
{
    Task<RefreshSessionResult> ExecuteAsync(string rawRefreshToken, CancellationToken ct = default);
}
