using System.Globalization;
using MediatR;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Features.Admin.PlatformReports;
using VietRide.Payment.Application.Features.RevenueAnalytics.Core;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Application.Features.RevenueAnalytics.Admin;

public sealed class GetAdminRevenueAnalyticsQueryHandler
    : IRequestHandler<GetAdminRevenueAnalyticsQuery, AdminRevenueAnalyticsResponse>
{
    private readonly IRevenueAnalyticsRepository repository;
    private readonly IIdentityOperatorSummaryClient identity;
    private readonly ITripRevenueAnalyticsClient trip;
    private readonly IClock clock;
    private readonly IRevenueReportCache cache;

    public GetAdminRevenueAnalyticsQueryHandler(
        IRevenueAnalyticsRepository repository,
        IIdentityOperatorSummaryClient identity,
        ITripRevenueAnalyticsClient trip,
        IClock clock,
        IRevenueReportCache cache)
    {
        this.repository = repository;
        this.identity = identity;
        this.trip = trip;
        this.clock = clock;
        this.cache = cache;
    }

    public async Task<AdminRevenueAnalyticsResponse> Handle(
        GetAdminRevenueAnalyticsQuery request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.GroupBy, "month", StringComparison.Ordinal))
        {
            throw Validation("groupBy", "groupBy must be month.");
        }

        var range = RevenueAnalyticsPeriodRules.AdminRange(
            ParseDate(request.From, "from"),
            ParseDate(request.To, "to"));
        var top = RevenueAnalyticsPeriodRules.ClampTop(request.Top);
        var cacheKey = RevenueReportCacheKeys.AdminAnalytics(range, top);
        var cached = await cache.GetAsync<AdminRevenueAnalyticsResponse>(cacheKey, cancellationToken);
        if (cached is not null)
            return cached;
        var currentRows = await repository.GetAdminMonthlyRevenueAsync(
            range.FromUtc,
            range.ToUtc,
            cancellationToken);
        var previousRows = await repository.GetAdminMonthlyRevenueAsync(
            range.PreviousFromUtc,
            range.PreviousToUtc,
            cancellationToken);
        var topRows = await repository.GetTopOperatorRevenueAsync(
            range.FromUtc,
            range.ToUtc,
            top,
            cancellationToken);

        var current = Sum(currentRows);
        var previous = Sum(previousRows);
        var monthly = BuildMonthly(range.From, range.To, currentRows);
        var topOperators = await EnrichTopOperatorsAsync(topRows, cancellationToken);

        var result = new AdminRevenueAnalyticsResponse(
            new AdminRevenuePeriod(range.From, range.To, RevenueAnalyticsPeriodRules.Timezone),
            new AdminRevenueSummary(
                new AdminRevenueComparisons(
                    RevenueComparisonFactory.Create(current.TotalProject, previous.TotalProject),
                    RevenueComparisonFactory.Create(current.NetTransport, previous.NetTransport),
                    RevenueComparisonFactory.Create(current.NetTicket, previous.NetTicket),
                    RevenueComparisonFactory.Create(current.NetParcel, previous.NetParcel),
                    RevenueComparisonFactory.Create(current.Subscription, previous.Subscription)),
                new AdminSettlementComparisons(
                    RevenueComparisonFactory.Create(current.Paid, previous.Paid))),
            monthly,
            topOperators,
            clock.UtcNow);
        await cache.SetAsync(cacheKey, result, RevenueReportCacheKeys.Expiration, cancellationToken);
        return result;
    }

    private async Task<IReadOnlyList<AdminTopOperatorItem>> EnrichTopOperatorsAsync(
        IReadOnlyList<TopOperatorRevenueReadModel> rows,
        CancellationToken cancellationToken)
    {
        var orderedRows = rows
            .OrderByDescending(row => row.RevenueVnd)
            .ThenBy(row => row.OperatorId)
            .ToArray();
        if (orderedRows.Length == 0)
        {
            return [];
        }

        var operatorIds = orderedRows.Select(row => row.OperatorId).ToArray();
        var identityTask = identity.GetAsync(operatorIds, cancellationToken);
        var vehicleTask = trip.GetVehicleCountsAsync(operatorIds, cancellationToken);
        await Task.WhenAll(identityTask, vehicleTask);
        var operatorSummaries = await identityTask;
        var vehicleCounts = await vehicleTask;
        if (operatorSummaries.Count != operatorIds.Length
            || vehicleCounts.Count != operatorIds.Length
            || operatorSummaries.Select(item => item.OperatorId).Distinct().Count() != operatorIds.Length
            || vehicleCounts.Select(item => item.OperatorId).Distinct().Count() != operatorIds.Length)
        {
            throw new UpstreamUnavailableException();
        }

        var summaryById = operatorSummaries.ToDictionary(item => item.OperatorId);
        var countById = vehicleCounts.ToDictionary(item => item.OperatorId);
        if (operatorIds.Any(id => !summaryById.ContainsKey(id) || !countById.ContainsKey(id)))
        {
            throw new UpstreamUnavailableException();
        }

        return orderedRows
            .Select((row, index) => new AdminTopOperatorItem(
                index + 1,
                row.OperatorId,
                summaryById[row.OperatorId].OperatorName,
                summaryById[row.OperatorId].LogoUrl,
                row.RevenueVnd,
                countById[row.OperatorId].VehicleCount))
            .ToArray();
    }

    private static IReadOnlyList<AdminRevenueMonthItem> BuildMonthly(
        DateOnly from,
        DateOnly to,
        IReadOnlyList<AdminRevenueMonthReadModel> rows)
    {
        var byMonth = rows.ToDictionary(row => row.Month);
        var current = new DateOnly(from.Year, from.Month, 1);
        var last = new DateOnly(to.Year, to.Month, 1);
        var result = new List<AdminRevenueMonthItem>();
        while (true)
        {
            byMonth.TryGetValue(current, out var row);
            var ticket = row?.NetTicketRevenueVnd ?? 0;
            var parcel = row?.NetParcelRevenueVnd ?? 0;
            var subscription = row?.SubscriptionRevenueVnd ?? 0;
            var paid = row?.PaidToOperatorsVnd ?? 0;
            var transport = checked(ticket + parcel);
            result.Add(new AdminRevenueMonthItem(
                current.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                new AdminRevenueMonthValues(
                    checked(transport + subscription),
                    transport,
                    ticket,
                    parcel,
                    subscription),
                new AdminSettlementMonthValues(paid)));
            if (current == last)
            {
                break;
            }

            current = current.AddMonths(1);
        }

        return result;
    }

    private static (long NetTicket, long NetParcel, long NetTransport, long Subscription, long TotalProject, long Paid) Sum(
        IReadOnlyList<AdminRevenueMonthReadModel> rows)
    {
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
        return (ticket, parcel, transport, subscription, checked(transport + subscription), paid);
    }

    private static DateOnly? ParseDate(string? value, string field)
    {
        if (value is null)
        {
            return null;
        }

        if (!DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            throw Validation(field, $"{field} must use YYYY-MM-DD format.");
        }

        return parsed;
    }

    private static CodedValidationException Validation(string field, string message)
        => new("VALIDATION_ERROR", message, [new ValidationError(field, message)]);
}
