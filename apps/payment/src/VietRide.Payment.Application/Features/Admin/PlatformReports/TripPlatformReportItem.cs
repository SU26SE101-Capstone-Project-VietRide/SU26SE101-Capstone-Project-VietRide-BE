namespace VietRide.Payment.Application.Features.Admin.PlatformReports;

public sealed record TripPlatformReportItem(
    Guid OperatorId,
    long CompletedTripCount);
