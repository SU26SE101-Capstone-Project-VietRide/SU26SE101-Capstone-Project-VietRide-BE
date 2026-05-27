namespace VietRide.Shared.Kernel.Abstractions;

/// <summary>
/// Issues short-lived Internal JWT tokens (HS256, TTL 120s) used in the
/// <c>X-Internal-Auth: Bearer &lt;token&gt;</c> header for service-to-service
/// calls. Per BACKEND_SOURCE_OF_TRUTH section 5.3.
/// </summary>
/// <remarks>
/// Concrete impl lives in <c>VietRide.Shared.Web.Authentication</c>. The
/// interface is declared in Kernel so non-Web libraries (e.g.
/// <c>VietRide.Shared.Http</c> delegating handlers) can depend on it
/// without creating a circular project reference.
/// </remarks>
public interface IInternalJwtTokenProvider
{
    /// <summary>
    /// Mints a fresh Internal JWT (HS256) for the given outbound call.
    /// </summary>
    /// <param name="subject">
    /// Subject claim — typically the originating user id, or the calling
    /// service name when no user context exists (system-initiated job).
    /// </param>
    /// <param name="audience">
    /// Optional override of the default audience claim
    /// (<c>vietride-internal</c>).
    /// </param>
    /// <returns>Compact JWT string (without the <c>Bearer </c> prefix).</returns>
    string IssueToken(string subject, string? audience = null);
}
