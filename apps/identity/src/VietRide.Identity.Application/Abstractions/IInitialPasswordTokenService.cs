using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Application.Abstractions;

public interface IInitialPasswordTokenService
{
    string GenerateCode();

    DateTimeOffset GetExpiresAt(DateTimeOffset now);

    /// <summary>
    /// Builds the "set your password" link embedded in the account-created email.
    /// </summary>
    /// <param name="role">
    /// Recipient's role. It selects the landing page: DRIVER/ASSISTANT onboard through
    /// the mobile app, every other role through the operator web. Passing the wrong role
    /// sends the user to a page they cannot complete onboarding on.
    /// </param>
    string BuildSetInitialPasswordUrl(string code, UserRole role);
}
