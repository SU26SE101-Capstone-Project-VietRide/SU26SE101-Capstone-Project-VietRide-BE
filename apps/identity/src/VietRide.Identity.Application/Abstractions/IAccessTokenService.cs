using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Application.Abstractions;

/// <summary>
/// Issues RS256 JWT access tokens for authenticated users.
/// Issuer: <c>vietride-identity</c>. Audience: <c>vietride-api</c>. TTL: 15 minutes.
/// </summary>
public interface IAccessTokenService
{
    /// <summary>
    /// Issues a signed RS256 JWT for the given user.
    /// Claims: iss, sub, role, operatorId, email, iat, exp, kid.
    /// </summary>
    string IssueToken(User user, OperatorRegistrationStatus? operatorStatus = null);
}
