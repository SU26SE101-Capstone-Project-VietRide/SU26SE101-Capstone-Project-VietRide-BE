using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.Application.Features.Admin.PlatformReports;

public sealed class PlatformReportValueOverflowException : Exception, ICodedHttpException
{
    public PlatformReportValueOverflowException(Exception? innerException = null)
        : base("A platform report aggregate exceeds the supported Int64 range.", innerException)
    {
    }

    public int StatusCode => 500;
    public string ErrorCode => "REPORT_VALUE_OVERFLOW";
}
