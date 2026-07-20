using VietRide.Shared.Application.Cqrs;

namespace VietRide.Booking.Application.Features.Admin.PlatformReports;

public sealed record GetPlatformReportQuery(string? From, string? To)
    : IQuery<PlatformReportResult>;
