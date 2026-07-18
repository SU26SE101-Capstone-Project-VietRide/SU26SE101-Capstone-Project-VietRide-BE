using VietRide.Shared.Application.Cqrs;

namespace VietRide.Booking.Application.Features.Internal.Reports.PlatformBookings;

public sealed record GetPlatformBookingReportQuery(string? From, string? To)
    : IQuery<PlatformBookingReportResult>;
