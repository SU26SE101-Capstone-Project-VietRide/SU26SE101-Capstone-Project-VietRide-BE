using MediatR;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Stations;

public sealed class GetAdminStationSummaryHandler(IStationRepository stations)
    : IRequestHandler<GetAdminStationSummaryQuery, AdminStationSummaryDto>
{
    public Task<AdminStationSummaryDto> Handle(
        GetAdminStationSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var query = stations.QueryNoTracking();
        return Task.FromResult(new AdminStationSummaryDto(
            query.LongCount(),
            query.LongCount(station => station.IsActive),
            query.LongCount(station => !station.IsActive),
            query.LongCount(station => station.SupportsShuttle)));
    }
}
