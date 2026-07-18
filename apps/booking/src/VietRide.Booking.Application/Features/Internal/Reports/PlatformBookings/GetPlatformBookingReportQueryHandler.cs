using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;

namespace VietRide.Booking.Application.Features.Internal.Reports.PlatformBookings;

public sealed class GetPlatformBookingReportQueryHandler
    : IRequestHandler<GetPlatformBookingReportQuery, PlatformBookingReportResult>
{
    private readonly IBookingRepository _bookings;

    public GetPlatformBookingReportQueryHandler(IBookingRepository bookings)
    {
        _bookings = bookings;
    }

    public async Task<PlatformBookingReportResult> Handle(
        GetPlatformBookingReportQuery request,
        CancellationToken cancellationToken)
    {
        var range = PlatformReportUtcRange.Parse(request.From, request.To);
        var items = await _bookings.GetPlatformBookingMetricsAsync(
            range.From,
            range.To,
            cancellationToken);
        return new PlatformBookingReportResult(items);
    }
}
