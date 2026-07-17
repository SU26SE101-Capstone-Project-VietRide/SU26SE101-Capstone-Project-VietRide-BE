using VietRide.Shared.Application.Cqrs;

namespace VietRide.Trip.Application.Features.Internal.Reports.PlatformTrips;

public sealed record GetPlatformTripReportQuery(string? From, string? To)
    : IQuery<PlatformTripReportResult>;
