using Google.Apis.Auth;
using Microsoft.Extensions.Options;
using VietRide.Identity.Application.Abstractions;

namespace VietRide.Identity.Infrastructure.Security;

public sealed class GoogleIdTokenVerifier : IGoogleIdTokenVerifier
{
    private readonly GoogleOAuthOptions _options;
    private readonly Func<string, GoogleJsonWebSignature.ValidationSettings, Task<GoogleJsonWebSignature.Payload>> _validateAsync;

    public GoogleIdTokenVerifier(IOptions<GoogleOAuthOptions> options)
        : this(options, GoogleJsonWebSignature.ValidateAsync)
    {
    }

    public GoogleIdTokenVerifier(
        IOptions<GoogleOAuthOptions> options,
        Func<string, GoogleJsonWebSignature.ValidationSettings, Task<GoogleJsonWebSignature.Payload>> validateAsync)
    {
        _options = options.Value;
        _validateAsync = validateAsync;
    }

    public async Task<GoogleIdTokenVerificationResult> VerifyAsync(
        string idToken,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idToken))
            throw new InvalidJwtException("Google ID token is required.");

        if (string.IsNullOrWhiteSpace(_options.ClientId))
            throw new InvalidOperationException("Google OAuth client id is not configured.");

        var settings = new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = [_options.ClientId]
        };

        GoogleJsonWebSignature.Payload payload;
        try
        {
            ct.ThrowIfCancellationRequested();
            payload = await _validateAsync(idToken, settings).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException)
        {
            throw new InvalidJwtException("Google ID token is invalid.");
        }

        if (string.IsNullOrWhiteSpace(payload.Subject) || string.IsNullOrWhiteSpace(payload.Email))
            throw new InvalidJwtException("Google ID token is missing required claims.");

        if (payload.EmailVerified != true)
            throw new InvalidJwtException("Google ID token email is not verified.");

        return new GoogleIdTokenVerificationResult(
            payload.Subject,
            payload.Email,
            payload.Name,
            payload.Picture);
    }
}
