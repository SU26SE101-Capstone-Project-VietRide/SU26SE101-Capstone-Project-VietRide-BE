using Microsoft.AspNetCore.Http;

namespace VietRide.Shared.Http.Handlers;

/// <summary>
/// Forwards the inbound <c>X-Request-Id</c> header onto every outbound
/// service call so trace/log lines stay correlated across hops. If the
/// inbound context has no id (background job, test) one is generated.
/// Per BACKEND_SOURCE_OF_TRUTH 5.3.
/// </summary>
public sealed class CorrelationIdDelegatingHandler : DelegatingHandler
{
    public const string HeaderName = "X-Request-Id";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public CorrelationIdDelegatingHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(HeaderName))
        {
            var inbound = _httpContextAccessor.HttpContext?.Request.Headers[HeaderName].ToString();
            var requestId = string.IsNullOrWhiteSpace(inbound)
                ? Guid.NewGuid().ToString("D")
                : inbound!;
            request.Headers.TryAddWithoutValidation(HeaderName, requestId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
