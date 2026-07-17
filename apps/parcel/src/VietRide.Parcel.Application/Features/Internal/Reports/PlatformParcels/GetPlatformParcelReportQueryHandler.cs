using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;

namespace VietRide.Parcel.Application.Features.Internal.Reports.PlatformParcels;

public sealed class GetPlatformParcelReportQueryHandler
    : IRequestHandler<GetPlatformParcelReportQuery, PlatformParcelReportResult>
{
    private readonly IParcelRepository _parcels;

    public GetPlatformParcelReportQueryHandler(IParcelRepository parcels)
    {
        _parcels = parcels;
    }

    public async Task<PlatformParcelReportResult> Handle(
        GetPlatformParcelReportQuery request,
        CancellationToken cancellationToken)
    {
        var range = PlatformReportUtcRange.Parse(request.From, request.To);
        var items = await _parcels.GetPlatformParcelMetricsAsync(
            range.From,
            range.To,
            cancellationToken);
        return new PlatformParcelReportResult(items);
    }
}
