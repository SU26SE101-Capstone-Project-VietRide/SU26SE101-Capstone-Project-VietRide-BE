using MediatR;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Auth.Logout;

public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand, Unit>
{
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IRefreshTokenFactory _refreshTokenFactory;
    private readonly IClock _clock;

    public LogoutCommandHandler(
        IRefreshTokenRepository refreshTokens,
        IRefreshTokenFactory refreshTokenFactory,
        IClock clock)
    {
        _refreshTokens = refreshTokens;
        _refreshTokenFactory = refreshTokenFactory;
        _clock = clock;
    }

    public async Task<Unit> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var hash = _refreshTokenFactory.ComputeHash(request.RefreshToken);
        var token = await _refreshTokens.GetByTokenHashAsync(hash, cancellationToken);

        // If token is not found or already revoked, treat as no-op (idempotent logout).
        if (token is null || token.RevokedAt is not null)
            return Unit.Value;

        token.Revoke(_clock.UtcNow, RefreshTokenRevokeReason.USER_LOGOUT);
        return Unit.Value;
    }
}
