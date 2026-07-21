namespace VietRide.Identity.Application.Abstractions;

public interface IFirebaseAuthService
{
    Task<string> CreateOperatorCustomTokenAsync(
        Guid userId,
        Guid operatorId,
        CancellationToken cancellationToken = default);

    Task RevokeRefreshTokensAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
