using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.Internal.Reports.PlatformBookings;

public sealed class PlatformReportValueOverflowException : Exception, ICodedHttpException
{
    public PlatformReportValueOverflowException()
        : base("A platform report aggregate exceeds the supported Int64 range.")
    {
    }

    public PlatformReportValueOverflowException(Exception innerException)
        : base("A platform report aggregate exceeds the supported Int64 range.", innerException)
    {
    }

    public int StatusCode => 500;
    public string ErrorCode => "REPORT_VALUE_OVERFLOW";
}
