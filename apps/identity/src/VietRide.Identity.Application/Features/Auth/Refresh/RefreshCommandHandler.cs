using MediatR;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Auth.Login;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Auth.Refresh;

public sealed class RefreshCommandHandler : IRequestHandler<RefreshCommand, TokenBundleDto>
{
    private const int AccessTokenTtlSeconds = 900; // 15 minutes
    private static readonly TimeSpan RotationGracePeriod = TimeSpan.FromSeconds(30);

    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IAccessTokenService _accessTokenService;
    private readonly IRefreshTokenFactory _refreshTokenFactory;
    private readonly IRefreshTokenFamilyRevoker _refreshTokenFamilyRevoker;
    private readonly IClock _clock;

    public RefreshCommandHandler(
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IAccessTokenService accessTokenService,
        IRefreshTokenFactory refreshTokenFactory,
        IRefreshTokenFamilyRevoker refreshTokenFamilyRevoker,
        IClock clock)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _accessTokenService = accessTokenService;
        _refreshTokenFactory = refreshTokenFactory;
        _refreshTokenFamilyRevoker = refreshTokenFamilyRevoker;
        _clock = clock;
    }

    public async Task<TokenBundleDto> Handle(
        RefreshCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Hash the incoming raw token for DB lookup.
        var hash = _refreshTokenFactory.ComputeHash(request.RefreshToken);
        var existing = await _refreshTokens.GetByTokenHashAsync(hash, cancellationToken);

        if (existing is null)
            throw new UnauthorizedException("AUTH_TOKEN_INVALID", "Refresh token is invalid.");

        // 2. Reuse detection. NORMAL_ROTATION gets a 30-second grace window for
        // parallel mobile refresh. Raw refresh tokens are never stored, so inside
        // grace we return 401 but deliberately do NOT revoke the family. After grace,
        // presenting the revoked parent is a reuse attack.
        if (existing.RevokedAt is not null)
        {
            await HandleRevokedTokenAsync(existing, cancellationToken);
        }

        // 3. Check expiry.
        if (existing.ExpiresAt <= _clock.UtcNow)
            throw new UnauthorizedException("AUTH_TOKEN_INVALID", "Refresh token has expired.");

        // 4. Load user and enforce current account status. LOCKED/DELETED/PENDING
        // users cannot refresh; do not rotate or revoke the presented token on this path.
        var user = await _users.GetByIdAsync(existing.UserId, cancellationToken)
            ?? throw new UnauthorizedException("AUTH_TOKEN_INVALID", "User not found.");

        if (user.Status != UserStatus.ACTIVE)
            throw new UnauthorizedException("AUTH_TOKEN_INVALID", "Refresh token is invalid for the current account status.");

        // 5. Revoke the consumed token (NORMAL_ROTATION).
        existing.Revoke(_clock.UtcNow, RefreshTokenRevokeReason.NORMAL_ROTATION);

        // 6. Issue new token in the same family.
        var (rawRefresh, newRefreshEntity) = _refreshTokenFactory.Create(
            userId: user.Id,
            parentTokenId: existing.Id,
            familyId: existing.FamilyId);

        await _refreshTokens.AddAsync(newRefreshEntity, cancellationToken);

        var accessToken = _accessTokenService.IssueToken(user);

        return new TokenBundleDto(
            AccessToken: accessToken,
            RefreshToken: rawRefresh,
            ExpiresInSeconds: AccessTokenTtlSeconds,
            User: new UserSummaryDto(
                Id: user.Id,
                Email: user.Email,
                DisplayName: user.DisplayName,
                Role: user.Role.ToString(),
                OperatorId: user.OperatorId,
                Status: user.Status.ToString()));
    }

    private async Task HandleRevokedTokenAsync(
        RefreshToken existing,
        CancellationToken cancellationToken)
    {
        if (existing.RevokedReason == RefreshTokenRevokeReason.NORMAL_ROTATION
            && existing.RevokedAt is { } revokedAt
            && _clock.UtcNow - revokedAt <= RotationGracePeriod)
        {
            // Raw refresh tokens cannot be replayed because only TokenHash is
            // persisted. Return unauthorized without family revocation so the first
            // successful refresh remains usable.
            throw new UnauthorizedException("AUTH_TOKEN_INVALID", "Refresh token was already rotated. Retry with the latest refresh token.");
        }

        await _refreshTokenFamilyRevoker.RevokeForReuseAsync(existing.FamilyId, cancellationToken);

        throw new UnauthorizedException("AUTH_TOKEN_INVALID", "Refresh token has already been used.");
    }
}
