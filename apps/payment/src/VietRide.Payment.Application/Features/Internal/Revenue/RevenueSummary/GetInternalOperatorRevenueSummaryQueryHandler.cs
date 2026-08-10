using MediatR;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Features.RevenueAnalytics.Core;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Application.Features.Internal.Revenue.RevenueSummary;

public sealed class GetInternalOperatorRevenueSummaryQueryHandler
    : IRequestHandler<GetInternalOperatorRevenueSummaryQuery, InternalOperatorRevenueSummaryResult>
{
    private readonly IRevenueAnalyticsRepository repository;
    private readonly IRevenueReportCache cache;
    private readonly IClock clock;

    public GetInternalOperatorRevenueSummaryQueryHandler(
        IRevenueAnalyticsRepository repository,
        IRevenueReportCache cache,
        IClock clock)
    {
        this.repository = repository;
        this.cache = cache;
        this.clock = clock;
    }

    public async Task<InternalOperatorRevenueSummaryResult> Handle(
        GetInternalOperatorRevenueSummaryQuery request,
        CancellationToken cancellationToken)
    {
        if (request.OperatorId == Guid.Empty)
            throw new CodedValidationException("VALIDATION_ERROR", "operatorId must be non-empty.");

        var range = InternalRevenueRangeParser.Parse(request.From, request.To);
        var key = RevenueReportCacheKeys.InternalOperatorSummary(request.OperatorId, range);
        var cached = await cache.GetAsync<InternalOperatorRevenueSummaryResult>(key, cancellationToken);
        if (cached is not null)
            return cached;

        var summary = await repository.GetOperatorRevenueSummaryAsync(
            request.OperatorId,
            range.FromUtc,
            range.ToUtc,
            cancellationToken);
        var result = new InternalOperatorRevenueSummaryResult(
            new InternalRevenuePeriod(range.From, range.To, RevenueAnalyticsPeriodRules.Timezone),
            request.OperatorId,
            checked(summary.NetTicketRevenueVnd + summary.NetParcelRevenueVnd),
            summary.NetTicketRevenueVnd,
            summary.NetParcelRevenueVnd,
            summary.GrossParcelRevenueVnd,
            summary.ParcelRefundsVnd,
            clock.UtcNow);
        await cache.SetAsync(key, result, RevenueReportCacheKeys.Expiration, cancellationToken);
        return result;
    }
}
