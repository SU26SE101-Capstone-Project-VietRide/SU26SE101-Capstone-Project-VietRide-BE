using MediatR;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Features.RevenueAnalytics.Core;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Application.Features.Internal.Revenue.RevenueSummary;

public sealed class GetInternalAdminRevenueSummaryQueryHandler
    : IRequestHandler<GetInternalAdminRevenueSummaryQuery, InternalAdminRevenueSummaryResult>
{
    private readonly IRevenueAnalyticsRepository repository;
    private readonly IRevenueReportCache cache;
    private readonly IClock clock;

    public GetInternalAdminRevenueSummaryQueryHandler(
        IRevenueAnalyticsRepository repository,
        IRevenueReportCache cache,
        IClock clock)
    {
        this.repository = repository;
        this.cache = cache;
        this.clock = clock;
    }

    public async Task<InternalAdminRevenueSummaryResult> Handle(
        GetInternalAdminRevenueSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var range = InternalRevenueRangeParser.Parse(request.From, request.To);
        var key = RevenueReportCacheKeys.InternalAdminSummary(range);
        var cached = await cache.GetAsync<InternalAdminRevenueSummaryResult>(key, cancellationToken);
        if (cached is not null)
            return cached;

        var rows = await repository.GetAdminMonthlyRevenueAsync(range.FromUtc, range.ToUtc, cancellationToken);
        long ticket = 0;
        long parcel = 0;
        long subscription = 0;
        long paid = 0;
        foreach (var row in rows)
        {
            ticket = checked(ticket + row.NetTicketRevenueVnd);
            parcel = checked(parcel + row.NetParcelRevenueVnd);
            subscription = checked(subscription + row.SubscriptionRevenueVnd);
            paid = checked(paid + row.PaidToOperatorsVnd);
        }

        var transport = checked(ticket + parcel);
        var result = new InternalAdminRevenueSummaryResult(
            new InternalRevenuePeriod(range.From, range.To, RevenueAnalyticsPeriodRules.Timezone),
            checked(transport + subscription),
            transport,
            ticket,
            parcel,
            subscription,
            paid,
            clock.UtcNow.UtcDateTime);
        await cache.SetAsync(key, result, RevenueReportCacheKeys.Expiration, cancellationToken);
        return result;
    }
}
