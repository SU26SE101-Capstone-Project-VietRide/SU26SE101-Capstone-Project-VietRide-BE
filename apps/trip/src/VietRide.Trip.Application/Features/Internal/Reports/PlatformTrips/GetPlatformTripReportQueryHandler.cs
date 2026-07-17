using MediatR;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Internal.Reports.PlatformTrips;

public sealed class GetPlatformTripReportQueryHandler
    : IRequestHandler<GetPlatformTripReportQuery, PlatformTripReportResult>
{
    private readonly ITripRepository _trips;

    public GetPlatformTripReportQueryHandler(ITripRepository trips)
    {
        _trips = trips;
    }

    public async Task<PlatformTripReportResult> Handle(
        GetPlatformTripReportQuery request,
        CancellationToken cancellationToken)
    {
        var range = PlatformReportUtcRange.Parse(request.From, request.To);
        var items = await _trips.GetPlatformTripMetricsAsync(
            range.From,
            range.To,
            cancellationToken);
        return new PlatformTripReportResult(items);
    }
}
