using VietRide.Shared.Application.Exceptions;

namespace VietRide.Trip.Application.Features.Internal.Reports.PlatformTrips;

public sealed class PlatformTripStatsMismatchException : Exception, ICodedHttpException
{
    public PlatformTripStatsMismatchException()
        : base("The materialized TripStats projection does not match the earned live source.")
    {
    }

    public int StatusCode => 503;
    public string ErrorCode => "UPSTREAM_UNAVAILABLE";
}
