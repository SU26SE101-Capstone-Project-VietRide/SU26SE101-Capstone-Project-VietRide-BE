using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Time;

namespace VietRide.Parcel.Application.Features.ParcelRouteFares.Summary;

public sealed class GetParcelRouteFareSummaryQueryHandler(
    IParcelRouteFareRepository fares,
    IClock clock) : IRequestHandler<GetParcelRouteFareSummaryQuery, IReadOnlyList<ParcelRouteFareSummaryItem>>
{
    public async Task<IReadOnlyList<ParcelRouteFareSummaryItem>> Handle(
        GetParcelRouteFareSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var today = BusinessTime.ToLocalDate(clock.UtcNow);
        var range = BusinessTime.GetUtcDayRange(today);
        var rows = await fares.QueryNoTracking()
            .Where(fare => fare.OperatorId == request.OperatorId)
            .OrderBy(fare => fare.RouteId)
            .ThenBy(fare => fare.SizeCategory)
            .ToListAsync(cancellationToken);

        return rows.GroupBy(fare => fare.RouteId)
            .Select(group => new ParcelRouteFareSummaryItem(
                group.Key,
                group.Select(fare => fare.SizeCategory.ToString()).Distinct().ToArray(),
                group.Any(fare => fare.EffectiveFrom < range.ToUtcExclusive
                    && (!fare.EffectiveUntil.HasValue || fare.EffectiveUntil >= range.FromUtc)),
                group.Any(fare => fare.EffectiveFrom >= range.ToUtcExclusive)))
            .ToArray();
    }
}
