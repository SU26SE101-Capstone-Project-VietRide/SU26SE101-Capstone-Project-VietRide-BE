namespace VietRide.Trip.Application.Features.Internal.Reports.PlatformTrips;

public sealed record PlatformTripReportItem(
    Guid OperatorId,
    long CompletedTripCount);
