using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Exceptions;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Infrastructure.ExternalClients;

public sealed class FirebaseAuthService : IFirebaseAuthService
{
    private readonly FirebaseAuthOptions _options;
    private readonly Lazy<FirebaseAuth> _auth;

    public FirebaseAuthService(FirebaseAuthOptions options)
    {
        _options = options;
        _auth = new Lazy<FirebaseAuth>(CreateAuth, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<string> CreateOperatorCustomTokenAsync(
        Guid userId,
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["operatorId"] = operatorId.ToString("D"),
                ["role"] = UserRole.OPERATOR_ADMIN.ToString(),
            };

            return await _auth.Value.CreateCustomTokenAsync(
                userId.ToString("D"),
                claims,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new FirebaseDependencyUnavailableException(
                "Firebase Auth is unavailable.",
                exception);
        }
    }

    public async Task RevokeRefreshTokensAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _auth.Value.RevokeRefreshTokensAsync(userId.ToString("D"), cancellationToken);
        }
        catch (FirebaseAuthException exception) when (exception.AuthErrorCode == AuthErrorCode.UserNotFound)
        {
            // A custom token may never have been exchanged, so there may be no Firebase user yet.
        }
    }

    private FirebaseAuth CreateAuth()
    {
        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException(
                "FIREBASE_PROJECT_ID, FIREBASE_CLIENT_EMAIL, and FIREBASE_PRIVATE_KEY are required.");
        }

        var credential = new ServiceAccountCredential(
            new ServiceAccountCredential.Initializer(_options.ClientEmail)
            {
                ProjectId = _options.ProjectId,
            }.FromPrivateKey(_options.PrivateKey.Replace("\\n", "\n", StringComparison.Ordinal)));

        var app = FirebaseApp.Create(
            new AppOptions
            {
                ProjectId = _options.ProjectId,
                Credential = credential.ToGoogleCredential(),
            },
            $"vietride-identity-{Guid.NewGuid():N}");

        return FirebaseAuth.GetAuth(app);
    }
}
