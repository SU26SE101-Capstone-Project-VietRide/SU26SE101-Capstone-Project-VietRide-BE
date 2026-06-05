using MediatR;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Auth.Login;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
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
    private readonly IClock _clock;

    public GoogleLoginCommandHandler(
        IGoogleIdTokenVerifier googleIdTokenVerifier,
        IOAuthIdentityRepository oauthIdentities,
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IAccessTokenService accessTokenService,
        IRefreshTokenFactory refreshTokenFactory,
        IClock clock)
    {
        _googleIdTokenVerifier = googleIdTokenVerifier;
        _oauthIdentities = oauthIdentities;
        _users = users;
        _refreshTokens = refreshTokens;
        _accessTokenService = accessTokenService;
        _refreshTokenFactory = refreshTokenFactory;
        _clock = clock;
    }

    public async Task<TokenBundleDto> Handle(
        GoogleLoginCommand request,
        CancellationToken cancellationToken)
    {
        var googleUser = await VerifyGoogleTokenAsync(request.IdToken, cancellationToken);
        var providerSubject = googleUser.Subject.Trim();
        var emailLower = googleUser.Email.Trim().ToLowerInvariant();

        var user = await _oauthIdentities.GetUserByProviderSubjectAsync(
            OAuthProvider.GOOGLE,
            providerSubject,
            cancellationToken);

        if (user is null)
        {
            user = await _users.GetByEmailAsync(emailLower, cancellationToken);

            if (user is null)
            {
                user = User.CreateGoogleAccount(
                    emailLower,
                    string.IsNullOrWhiteSpace(googleUser.DisplayName) ? emailLower : googleUser.DisplayName,
                    googleUser.AvatarUrl);

                await _users.AddAsync(user, cancellationToken);
            }
            else
            {
                EnsureCanLogin(user);
            }

            var oauthIdentity = OAuthIdentity.Create(
                user.Id,
                OAuthProvider.GOOGLE,
                providerSubject,
                emailLower,
                _clock.UtcNow);

            await _oauthIdentities.AddAsync(oauthIdentity, cancellationToken);
        }
        else
        {
            EnsureCanLogin(user);
        }

        user.RecordSuccessfulLogin(_clock);

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

    private static void EnsureCanLogin(User user)
    {
        if (user.Status == UserStatus.LOCKED)
            throw new ForbiddenException("AUTH_ACCOUNT_LOCKED", "Account is locked. Please contact support.");

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
