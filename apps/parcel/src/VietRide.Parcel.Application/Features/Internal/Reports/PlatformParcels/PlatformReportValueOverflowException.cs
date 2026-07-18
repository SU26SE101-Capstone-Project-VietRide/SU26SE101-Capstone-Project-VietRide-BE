using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Internal.Reports.PlatformParcels;

public sealed class PlatformReportValueOverflowException : Exception, ICodedHttpException
{
    public PlatformReportValueOverflowException(Exception innerException)
        : base("A platform report aggregate exceeds the supported Int64 range.", innerException)
    {
    }

    public int StatusCode => 500;
    public string ErrorCode => "REPORT_VALUE_OVERFLOW";
}
