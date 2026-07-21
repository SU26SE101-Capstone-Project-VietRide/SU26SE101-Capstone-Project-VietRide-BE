using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.Internal.Reports.PlatformBookings;

public sealed class PlatformBookingStatsMismatchException : Exception, ICodedHttpException
{
    public PlatformBookingStatsMismatchException()
        : base("The materialized BookingStats projection does not match the earned live source.")
    {
    }

    public int StatusCode => 503;
    public string ErrorCode => "UPSTREAM_UNAVAILABLE";
}
