using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Events;
using VietRide.Identity.Application.Features.Auth.Login;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Auth.GoogleLogin;

public sealed class GoogleLoginCommandHandler : IRequestHandler<GoogleLoginCommand, TokenBundleDto>
{
    private const int AccessTokenTtlSeconds = 900; // 15 minutes

    private readonly IGoogleIdTokenVerifier _googleIdTokenVerifier;
    private readonly IOAuthIdentityRepository _oauthIdentities;
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IAccessTokenService _accessTokenService;
    private readonly IRefreshTokenFactory _refreshTokenFactory;
    private readonly ILoginLockoutCounter _loginLockoutCounter;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;
    private readonly IOperatorRepository _operators;
    private readonly ILogger<GoogleLoginCommandHandler> _logger;

    public GoogleLoginCommandHandler(
        IGoogleIdTokenVerifier googleIdTokenVerifier,
        IOAuthIdentityRepository oauthIdentities,
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IAccessTokenService accessTokenService,
        IRefreshTokenFactory refreshTokenFactory,
        ILoginLockoutCounter loginLockoutCounter,
        IIntegrationEventOutbox outbox,
        IClock clock,
        IOperatorRepository operators,
        ILogger<GoogleLoginCommandHandler>? logger = null)
    {
        _googleIdTokenVerifier = googleIdTokenVerifier;
        _oauthIdentities = oauthIdentities;
        _users = users;
        _refreshTokens = refreshTokens;
        _accessTokenService = accessTokenService;
        _refreshTokenFactory = refreshTokenFactory;
        _loginLockoutCounter = loginLockoutCounter;
        _outbox = outbox;
        _clock = clock;
        _operators = operators;
        _logger = logger ?? NullLogger<GoogleLoginCommandHandler>.Instance;
    }

    public async Task<TokenBundleDto> Handle(
        GoogleLoginCommand request,
        CancellationToken cancellationToken)
    {
        var googleUser = await VerifyGoogleTokenAsync(request.IdToken, cancellationToken);
        var providerSubject = googleUser.Subject.Trim();
        var emailLower = googleUser.Email.Trim().ToLowerInvariant();

        var userHint = await _oauthIdentities.GetUserByProviderSubjectAsync(
            OAuthProvider.GOOGLE,
            providerSubject,
            cancellationToken);
        var shouldCreateOAuthIdentity = false;
        var isNewUser = false;

        if (userHint is null)
        {
            userHint = await _users.GetByEmailAsync(emailLower, cancellationToken);

            if (userHint is null)
            {
                userHint = User.CreateGoogleAccount(
                    emailLower,
                    string.IsNullOrWhiteSpace(googleUser.DisplayName) ? emailLower : googleUser.DisplayName,
                    googleUser.AvatarUrl);

                await _users.AddAsync(userHint, cancellationToken);
                isNewUser = true;
            }

            shouldCreateOAuthIdentity = true;
        }

        var user = userHint;
        if (!isNewUser)
        {
            user = await _users.GetByIdForUpdateAsync(userHint.Id, cancellationToken)
                ?? throw new ForbiddenException("FORBIDDEN", "Account is not active.");
            if (user.Status == UserStatus.LOCKED)
            {
                _logger.LogWarning(
                    "AuthAccountLocked: Google login rejected for locked user {UserId}; ClientKind={ClientKind}",
                    user.Id,
                    request.ClientKind);
            }
            EnsureCanLogin(user);
            await _loginLockoutCounter.ResetAsync(user.Id, cancellationToken);
        }

        if (shouldCreateOAuthIdentity)
        {
            var oauthIdentity = OAuthIdentity.Create(
                user.Id,
                OAuthProvider.GOOGLE,
                providerSubject,
                emailLower,
                _clock.UtcNow);

            await _oauthIdentities.AddAsync(oauthIdentity, cancellationToken);
        }

        if (isNewUser)
        {
            var integrationEvent = new UserCreatedIntegrationEvent(
                user.Id,
                user.Role.ToString(),
                user.Email,
                _clock.UtcNow);
            await _outbox.EnqueueAsync(
                UserCreatedIntegrationEvent.EventType,
                JsonSerializer.Serialize(integrationEvent),
                cancellationToken);
        }

        user.RecordSuccessfulLogin(_clock);

        var operatorStatus = await ResolveOperatorSessionAsync(user, request.ClientKind, cancellationToken);
        var accessToken = _accessTokenService.IssueToken(user, operatorStatus);
        var (rawRefresh, refreshEntity) = _refreshTokenFactory.Create(
            userId: user.Id,
            parentTokenId: null,
            familyId: null);

        await _refreshTokens.AddAsync(refreshEntity, cancellationToken);

        _logger.LogInformation(
            "AuthLoginSucceeded: Google user {UserId} authenticated with operator status {OperatorStatus}; ClientKind={ClientKind}",
            user.Id,
            operatorStatus?.ToString() ?? "NONE",
            request.ClientKind);

        return new TokenBundleDto(
            AccessToken: accessToken,
            RefreshToken: rawRefresh,
            ExpiresInSeconds: AccessTokenTtlSeconds,
            User: new UserSummaryDto(
                Id: user.Id,
                Email: user.Email,
                Phone: user.Phone?.Value,
                DisplayName: user.DisplayName,
                Role: user.Role.ToString(),
                OperatorId: user.OperatorId,
                Status: user.Status.ToString(),
                OperatorRegistrationStatus: operatorStatus?.ToString(),
                AvatarUrl: user.AvatarUrl));
    }

    private async Task<OperatorRegistrationStatus?> ResolveOperatorSessionAsync(
        User user,
        string clientKind,
        CancellationToken cancellationToken)
    {
        if (!user.OperatorId.HasValue)
            return null;

        var operatorEntity = await _operators.GetByIdAsync(user.OperatorId.Value, cancellationToken);
        if (operatorEntity?.RegistrationStatus == OperatorRegistrationStatus.APPROVED && operatorEntity.IsActive)
            return OperatorRegistrationStatus.APPROVED;

        if (operatorEntity?.RegistrationStatus == OperatorRegistrationStatus.SUSPENDED)
        {
            if (user.Role == UserRole.OPERATOR_ADMIN)
            {
                _logger.LogWarning(
                    "OperatorRestrictedLogin: Google user {UserId} entered a restricted session for suspended operator {OperatorId}; ClientKind={ClientKind}",
                    user.Id,
                    operatorEntity.Id,
                    clientKind);
                return OperatorRegistrationStatus.SUSPENDED;
            }

            throw new ForbiddenException(
                "OPERATOR_SUSPENDED",
                "The operator is suspended. Only its administrator may access the suspension status page.");
        }

        throw new ForbiddenException("FORBIDDEN", "Operator registration is not approved.");
    }

    private static void EnsureCanLogin(User user)
    {
        if (user.Status == UserStatus.LOCKED)
        {
            throw new ForbiddenException("AUTH_ACCOUNT_LOCKED", "Account is locked. Please contact support.");
        }

        if (user.Status == UserStatus.PENDING_EMAIL_VERIFICATION)
            throw new ForbiddenException("AUTH_EMAIL_NOT_VERIFIED", "Email address has not been verified.");

        if (user.Status != UserStatus.ACTIVE)
            throw new ForbiddenException("FORBIDDEN", "Account is not active.");
    }

    private async Task<GoogleIdTokenVerificationResult> VerifyGoogleTokenAsync(
        string idToken,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _googleIdTokenVerifier.VerifyAsync(idToken, cancellationToken);
        }
        catch (Exception ex) when (IsGoogleJwtInvalid(ex))
        {
            throw new UnauthorizedException(
                "AUTH_GOOGLE_TOKEN_INVALID",
                "Google ID token signature, expiry, or audience is invalid.");
        }
    }

    private static bool IsGoogleJwtInvalid(Exception exception)
    {
        return exception.GetType().FullName == "Google.Apis.Auth.InvalidJwtException";
    }
}
