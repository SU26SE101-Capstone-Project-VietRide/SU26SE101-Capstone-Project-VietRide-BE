namespace VietRide.Identity.Application.Abstractions;

public interface IGoogleIdTokenVerifier
{
    Task<GoogleIdTokenVerificationResult> VerifyAsync(string idToken, CancellationToken ct);
}
