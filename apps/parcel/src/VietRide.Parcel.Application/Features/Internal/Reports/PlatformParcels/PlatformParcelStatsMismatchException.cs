using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Internal.Reports.PlatformParcels;

public sealed class PlatformParcelStatsMismatchException : Exception, ICodedHttpException
{
    public PlatformParcelStatsMismatchException()
        : base("The materialized ParcelStats projection does not match the earned live source.")
    {
    }

    public int StatusCode => 503;
    public string ErrorCode => "UPSTREAM_UNAVAILABLE";
}
