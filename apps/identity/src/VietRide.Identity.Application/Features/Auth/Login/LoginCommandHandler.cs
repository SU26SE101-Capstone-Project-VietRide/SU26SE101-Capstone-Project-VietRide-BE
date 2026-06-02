using MediatR;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Auth.Login;

public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, TokenBundleDto>
{
    private const int AccessTokenTtlSeconds = 900; // 15 minutes

    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHasher _hasher;
    private readonly IAccessTokenService _accessTokenService;
    private readonly IRefreshTokenFactory _refreshTokenFactory;
    private readonly IClock _clock;

    public LoginCommandHandler(
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IPasswordHasher hasher,
        IAccessTokenService accessTokenService,
        IRefreshTokenFactory refreshTokenFactory,
        IClock clock)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _hasher = hasher;
        _accessTokenService = accessTokenService;
        _refreshTokenFactory = refreshTokenFactory;
        _clock = clock;
    }

    public async Task<TokenBundleDto> Handle(
        LoginCommand request,
        CancellationToken cancellationToken)
    {
        var emailLower = request.Email.Trim().ToLowerInvariant();
        var user = await _users.GetByEmailAsync(emailLower, cancellationToken);

        // 1. Check if account is locked (403 — before credential check to avoid timing info leak).
        if (user?.Status == UserStatus.LOCKED)
            throw new ForbiddenException("AUTH_ACCOUNT_LOCKED", "Account is locked. Please contact support.");

        // 2. Check if email is not yet verified (403).
        if (user?.Status == UserStatus.PENDING_EMAIL_VERIFICATION)
            throw new ForbiddenException("AUTH_EMAIL_NOT_VERIFIED", "Email address has not been verified.");

        // 3. Validate credentials. Constant-time reject when user not found.
        var passwordValid = user is not null
            && user.PasswordHash is not null
            && _hasher.Verify(request.Password, user.PasswordHash);

        if (!passwordValid)
        {
            // Increment failed attempt counter if the user exists.
            if (user is not null)
            {
                user.RecordFailedLogin(_clock);
                // SaveChanges is committed by TransactionBehavior.
            }

            throw new UnauthorizedException("AUTH_INVALID_CREDENTIALS", "Invalid email or password.");
        }

        // 4. Successful login — reset failed counter and record timestamp.
        user!.RecordSuccessfulLogin(_clock);

        // 5. Issue tokens.
        var accessToken = _accessTokenService.IssueToken(user);
        var (rawRefresh, refreshEntity) = _refreshTokenFactory.Create(
            userId: user.Id,
            parentTokenId: null,
            familyId: null);

        await _refreshTokens.AddAsync(refreshEntity, cancellationToken);

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
