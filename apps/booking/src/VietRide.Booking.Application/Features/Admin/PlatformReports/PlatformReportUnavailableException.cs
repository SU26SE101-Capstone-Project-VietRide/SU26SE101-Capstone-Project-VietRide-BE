using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.Admin.PlatformReports;

public sealed class PlatformReportUnavailableException : Exception, ICodedHttpException
{
    public PlatformReportUnavailableException(Exception? innerException = null)
        : base("The platform report source is unavailable or returned an unusable response.", innerException)
    {
    }

    public int StatusCode => 503;
    public string ErrorCode => "UPSTREAM_UNAVAILABLE";
}
