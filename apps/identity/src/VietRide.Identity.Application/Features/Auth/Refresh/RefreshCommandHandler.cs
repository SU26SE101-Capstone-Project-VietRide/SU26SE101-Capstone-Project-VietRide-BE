using MediatR;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Auth.Login;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Auth.Refresh;

public sealed class RefreshCommandHandler : IRequestHandler<RefreshCommand, TokenBundleDto>
{
    private const int AccessTokenTtlSeconds = 900; // 15 minutes

    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IAccessTokenService _accessTokenService;
    private readonly IRefreshTokenFactory _refreshTokenFactory;
    private readonly IClock _clock;

    public RefreshCommandHandler(
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IAccessTokenService accessTokenService,
        IRefreshTokenFactory refreshTokenFactory,
        IClock clock)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _accessTokenService = accessTokenService;
        _refreshTokenFactory = refreshTokenFactory;
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

        // 2. Reuse detection: if the token is already revoked, revoke the whole family.
        if (existing.RevokedAt is not null)
        {
            await _refreshTokens.RevokeFamilyAsync(
                existing.FamilyId,
                RefreshTokenRevokeReason.REUSE_DETECTED,
                cancellationToken);

            throw new UnauthorizedException("AUTH_TOKEN_INVALID", "Refresh token has already been used.");
        }

        // 3. Check expiry.
        if (existing.ExpiresAt <= _clock.UtcNow)
            throw new UnauthorizedException("AUTH_TOKEN_INVALID", "Refresh token has expired.");

        // 4. Load user.
        var user = await _users.GetByIdAsync(existing.UserId, cancellationToken)
            ?? throw new UnauthorizedException("AUTH_TOKEN_INVALID", "User not found.");

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
}
