using MediatR;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Auth.Login;

public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, TokenBundleDto>
{
    private const int AccessTokenTtlSeconds = 900; // 15 minutes

    private readonly IUserRepository _users;
    private readonly IOperatorRepository _operators;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHasher _hasher;
    private readonly IAccessTokenService _accessTokenService;
    private readonly IRefreshTokenFactory _refreshTokenFactory;
    private readonly IFailedLoginPersister _failedLoginPersister;
    private readonly ILoginLockoutCounter _loginLockoutCounter;
    private readonly IClock _clock;

    public LoginCommandHandler(
        IUserRepository users,
        IOperatorRepository operators,
        IRefreshTokenRepository refreshTokens,
        IPasswordHasher hasher,
        IAccessTokenService accessTokenService,
        IRefreshTokenFactory refreshTokenFactory,
        IFailedLoginPersister failedLoginPersister,
        ILoginLockoutCounter loginLockoutCounter,
        IClock clock)
    {
        _users = users;
        _operators = operators;
        _refreshTokens = refreshTokens;
        _hasher = hasher;
        _accessTokenService = accessTokenService;
        _refreshTokenFactory = refreshTokenFactory;
        _failedLoginPersister = failedLoginPersister;
        _loginLockoutCounter = loginLockoutCounter;
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

        // 2. Passenger mobile may enter the app before email verification; FE restricts features via user.status.
        if (user?.Status == UserStatus.PENDING_EMAIL_VERIFICATION && user.Role != UserRole.PASSENGER)
            throw new ForbiddenException("AUTH_EMAIL_NOT_VERIFIED", "Email address has not been verified.");

        // 3. Check if the operator has not set an initial password yet (403).
        if (user?.Status == UserStatus.PENDING_INITIAL_PASSWORD)
            throw new ForbiddenException("AUTH_PENDING_INITIAL_PASSWORD", "Initial password has not been set.");

        // 4. Validate credentials. Constant-time reject when user not found.
        var passwordValid = user is not null
            && user.PasswordHash is not null
            && _hasher.Verify(request.Password, user.PasswordHash);

        if (!passwordValid)
        {
            // Increment the 15-minute Redis window for existing users only. Unknown-email
            // attempts still fail uniformly but must not create a user-scoped Redis key.
            if (user is not null)
            {
                var failedAttemptsInWindow = await _loginLockoutCounter.IncrementAsync(
                    user.Id,
                    cancellationToken);

                await _failedLoginPersister.PersistAsync(
                    user.Id,
                    failedAttemptsInWindow,
                    cancellationToken);
            }

            throw new UnauthorizedException("AUTH_INVALID_CREDENTIALS", "Invalid email or password.");
        }

        var authenticatedUser = user!;

        // 5. Operator dashboard login is allowed only after the operator is approved.
        if (RequiresApprovedOperator(authenticatedUser))
        {
            var operatorEntity = await _operators.GetByIdAsync(authenticatedUser.OperatorId!.Value, cancellationToken);
            if (operatorEntity?.RegistrationStatus != OperatorRegistrationStatus.APPROVED)
                throw new ForbiddenException("FORBIDDEN", "Operator registration is not approved.");
        }

        // 6. Successful login — reset DB tracking and clear the Redis lockout window.
        authenticatedUser.RecordSuccessfulLogin(_clock);
        await _loginLockoutCounter.ResetAsync(authenticatedUser.Id, cancellationToken);

        // 7. Issue tokens.
        var accessToken = _accessTokenService.IssueToken(authenticatedUser);
        var (rawRefresh, refreshEntity) = _refreshTokenFactory.Create(
            userId: authenticatedUser.Id,
            parentTokenId: null,
            familyId: null);

        await _refreshTokens.AddAsync(refreshEntity, cancellationToken);

        return new TokenBundleDto(
            AccessToken: accessToken,
            RefreshToken: rawRefresh,
            ExpiresInSeconds: AccessTokenTtlSeconds,
            User: new UserSummaryDto(
                Id: authenticatedUser.Id,
                Email: authenticatedUser.Email,
                Phone: authenticatedUser.Phone?.Value,
                DisplayName: authenticatedUser.DisplayName,
                Role: authenticatedUser.Role.ToString(),
                OperatorId: authenticatedUser.OperatorId,
                Status: authenticatedUser.Status.ToString()));
    }

    private static bool RequiresApprovedOperator(User user)
        => user.OperatorId.HasValue
            && (user.Role == UserRole.OPERATOR_ADMIN || user.Role == UserRole.OPERATOR_STAFF);
}
