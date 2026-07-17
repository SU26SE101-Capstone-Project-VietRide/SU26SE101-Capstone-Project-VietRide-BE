using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.Application.Features.Admin.PlatformReports;

public sealed class UpstreamUnavailableException : Exception, ICodedHttpException
{
    public UpstreamUnavailableException(Exception? innerException = null)
        : base("A required platform report upstream is unavailable or returned an unusable payload.", innerException)
    {
    }

    public int StatusCode => 502;
    public string ErrorCode => "UPSTREAM_UNAVAILABLE";
}
