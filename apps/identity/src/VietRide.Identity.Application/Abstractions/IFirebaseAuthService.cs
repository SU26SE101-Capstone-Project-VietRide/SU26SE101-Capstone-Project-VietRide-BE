namespace VietRide.Identity.Application.Abstractions;

public interface IFirebaseAuthService
{
    Task<string> CreateCustomTokenAsync(
        Guid userId,
        string role,
        Guid? operatorId,
        string uploadPurpose,
        CancellationToken cancellationToken = default);

    Task RevokeRefreshTokensAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
