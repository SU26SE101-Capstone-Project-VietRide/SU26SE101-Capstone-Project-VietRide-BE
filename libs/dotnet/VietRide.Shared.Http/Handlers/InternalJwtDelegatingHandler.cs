using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Shared.Http.Handlers;

/// <summary>
/// DelegatingHandler that mints a short-lived Internal JWT via
/// <see cref="IInternalJwtTokenProvider"/> and stamps it onto every
/// outbound request as <c>X-Internal-Auth: Bearer &lt;token&gt;</c>.
/// Per BACKEND_SOURCE_OF_TRUTH 5.3 and 5.1 (<c>/internal/v1/...</c>).
/// </summary>
/// <remarks>
/// The subject claim defaults to the originating user id pulled off the
/// inbound request's claims (<c>sub</c>) when available, otherwise the
/// constant <c>vietride-system</c> for jobs without a user context.
/// Token TTL is owned by the provider implementation (currently 120s).
/// </remarks>
public sealed class InternalJwtDelegatingHandler : DelegatingHandler
{
    public const string HeaderName = "X-Internal-Auth";
    public const string SystemSubject = "vietride-system";

    private readonly IInternalJwtTokenProvider _tokenProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<InternalJwtDelegatingHandler> _logger;

    public InternalJwtDelegatingHandler(
        IInternalJwtTokenProvider tokenProvider,
        IHttpContextAccessor httpContextAccessor,
        ILogger<InternalJwtDelegatingHandler> logger)
    {
        _tokenProvider = tokenProvider;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Don't overwrite if caller already set one (e.g. tests, or a
        // service explicitly forwarding the inbound token).
        if (!request.Headers.Contains(HeaderName))
        {
            var subject = ResolveSubject();
            var token = _tokenProvider.IssueToken(subject);
            request.Headers.TryAddWithoutValidation(HeaderName, $"Bearer {token}");
        }
        else
        {
            _logger.LogDebug(
                "Outbound request to {Uri} already carries {Header}; not overwriting.",
                request.RequestUri, HeaderName);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private string ResolveSubject()
    {
        var ctx = _httpContextAccessor.HttpContext;
        var sub = ctx?.User?.FindFirst("sub")?.Value
                  ?? ctx?.User?.Identity?.Name;
        return string.IsNullOrWhiteSpace(sub) ? SystemSubject : sub;
    }
}
