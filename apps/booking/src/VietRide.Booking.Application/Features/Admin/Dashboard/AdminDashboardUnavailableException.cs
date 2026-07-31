using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.Admin.Dashboard;

public sealed class AdminDashboardUnavailableException : Exception, ICodedHttpException
{
    public AdminDashboardUnavailableException(Exception? innerException = null)
        : base("Identity dashboard metrics are temporarily unavailable.", innerException)
    {
    }

    public int StatusCode => 503;
    public string ErrorCode => "UPSTREAM_UNAVAILABLE";
}
