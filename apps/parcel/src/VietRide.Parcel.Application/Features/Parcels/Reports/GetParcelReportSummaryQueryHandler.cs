using MediatR;
using VietRide.Parcel.Application.Abstractions.Caching;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.Reports;

public sealed class GetParcelReportSummaryQueryHandler
    : IRequestHandler<GetParcelReportSummaryQuery, ParcelReportSummaryResponse>
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IParcelStatsRepository statsRepository;
    private readonly IParcelRepository parcelRepository;
    private readonly IParcelReportCache cache;
    private readonly IClock clock;

    public GetParcelReportSummaryQueryHandler(
        IParcelStatsRepository statsRepository,
        IParcelRepository parcelRepository,
        IParcelReportCache cache,
        IClock clock)
    {
        this.statsRepository = statsRepository;
        this.parcelRepository = parcelRepository;
        this.cache = cache;
        this.clock = clock;
    }

    public async Task<ParcelReportSummaryResponse> Handle(
        GetParcelReportSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var (from, to) = ParcelReportQuerySupport.NormalizeRange(request.From, request.To, clock);
        var cacheKey = $"parcel:report:summary:{request.OperatorId}:{from:yyyyMMdd}:{to:yyyyMMdd}";
        var cached = await cache.GetAsync<ParcelReportSummaryResponse>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var response = await ParcelReportQuerySupport.BuildSummaryAsync(
            statsRepository,
            parcelRepository,
            request.OperatorId,
            from,
            to,
            cancellationToken);
        await cache.SetAsync(cacheKey, response, CacheTtl, cancellationToken);
        return response;
    }
}
