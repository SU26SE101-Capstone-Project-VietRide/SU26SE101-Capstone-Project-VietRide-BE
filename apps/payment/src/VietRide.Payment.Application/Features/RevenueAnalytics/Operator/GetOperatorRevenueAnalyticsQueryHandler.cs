using System.Globalization;
using MediatR;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Features.Admin.PlatformReports;
using VietRide.Payment.Application.Features.RevenueAnalytics.Core;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Application.Features.RevenueAnalytics.Operator;

public sealed class GetOperatorRevenueAnalyticsQueryHandler
    : IRequestHandler<GetOperatorRevenueAnalyticsQuery, OperatorRevenueAnalyticsResponse>
{
    private readonly IRevenueAnalyticsRepository repository;
    private readonly ITripRevenueAnalyticsClient trip;
    private readonly IClock clock;
    private readonly IRevenueReportCache cache;

    public GetOperatorRevenueAnalyticsQueryHandler(
        IRevenueAnalyticsRepository repository,
        ITripRevenueAnalyticsClient trip,
        IClock clock,
        IRevenueReportCache cache)
    {
        this.repository = repository;
        this.trip = trip;
        this.clock = clock;
        this.cache = cache;
    }

    public async Task<OperatorRevenueAnalyticsResponse> Handle(
        GetOperatorRevenueAnalyticsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.OperatorId == Guid.Empty)
        {
            throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        }

        var period = RevenueAnalyticsPeriodRules.OperatorPeriod(request.Month, request.Year, request.GroupBy);
        var cacheKey = RevenueReportCacheKeys.OperatorAnalytics(request.OperatorId, period);
        var cached = await cache.GetAsync<OperatorRevenueAnalyticsResponse>(cacheKey, cancellationToken);
        if (cached is not null)
            return cached;
        var rows = await repository.GetOperatorRevenueLedgerAsync(
            request.OperatorId,
            period.QueryFromUtc,
            period.CurrentToUtc,
            cancellationToken);
        var currentRows = rows.Where(row =>
            row.Month >= new DateOnly(period.From.Year, period.From.Month, 1)
            && row.Month <= new DateOnly(period.To.Year, period.To.Month, 1)).ToArray();
        var previousFrom = period.IsYearMode
            ? new DateOnly(period.From.Year - 1, 1, 1)
            : new DateOnly(period.From.AddMonths(-1).Year, period.From.AddMonths(-1).Month, 1);
        var previousTo = period.IsYearMode
            ? new DateOnly(period.From.Year - 1, 12, 1)
            : previousFrom;
        var previousRows = rows.Where(row => row.Month >= previousFrom && row.Month <= previousTo).ToArray();
        var tripIds = currentRows
            .Where(row => row.TripId.HasValue)
            .Select(row => row.TripId!.Value)
            .Distinct()
            .Order()
            .ToArray();
        var summaries = period.IsYearMode || tripIds.Length == 0
            ? Array.Empty<TripRevenueSummaryItem>()
            : await trip.GetTripSummariesAsync(tripIds, cancellationToken);
        var summaryByTrip = period.IsYearMode
            ? new Dictionary<Guid, TripRevenueSummaryItem>()
            : ValidateAndIndexSummaries(tripIds, summaries);

        var current = Sum(currentRows);
        var previous = Sum(previousRows);

        var result = new OperatorRevenueAnalyticsResponse(
            new OperatorRevenueAnalyticsPeriod(
                period.Month,
                period.Year,
                "month",
                period.From,
                period.To,
                RevenueAnalyticsPeriodRules.Timezone),
            new OperatorRevenueSummary(
                RevenueComparisonFactory.Create(current.Total, previous.Total),
                RevenueComparisonFactory.Create(current.Ticket, previous.Ticket),
                RevenueComparisonFactory.Create(current.Parcel, previous.Parcel),
                RevenueComparisonFactory.Create(
                    Average(current.Total, current.TripCount),
                    Average(previous.Total, previous.TripCount))),
            BuildMonthly(period.Months, rows),
            period.IsYearMode
                ? null
                : await BuildRoutePerformanceAsync(
                    request.OperatorId,
                    period.Month!,
                    currentRows,
                    summaryByTrip,
                    cancellationToken),
            clock.UtcNow);
        await cache.SetAsync(cacheKey, result, RevenueReportCacheKeys.Expiration, cancellationToken);
        return result;
    }

    private async Task<IReadOnlyList<OperatorRoutePerformanceItem>> BuildRoutePerformanceAsync(
        Guid operatorId,
        string month,
        IReadOnlyList<OperatorRevenueLedgerReadModel> currentRows,
        IReadOnlyDictionary<Guid, TripRevenueSummaryItem> summaryByTrip,
        CancellationToken cancellationToken)
    {
        var routes = await trip.GetRoutePerformanceAsync(operatorId, month, cancellationToken);
        ValidateRoutes(routes);
        return BuildRoutePerformance(routes, currentRows, summaryByTrip);
    }

    private static IReadOnlyList<OperatorRevenueMonthItem> BuildMonthly(
        IReadOnlyList<string> months,
        IReadOnlyList<OperatorRevenueLedgerReadModel> rows)
    {
        var groups = rows
            .GroupBy(row => row.Month)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var result = new List<OperatorRevenueMonthItem>(months.Count);
        foreach (var month in months)
        {
            var key = DateOnly.ParseExact(
                $"{month}-01",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None);
            var values = groups.GetValueOrDefault(key) ?? [];
            var total = Sum(values);
            result.Add(new OperatorRevenueMonthItem(
                month,
                total.Total,
                total.Ticket,
                total.Parcel,
                total.TripCount));
        }

        return result;
    }

    private static IReadOnlyList<OperatorRoutePerformanceItem> BuildRoutePerformance(
        IReadOnlyList<TripRoutePerformanceItem> routes,
        IReadOnlyList<OperatorRevenueLedgerReadModel> currentRows,
        IReadOnlyDictionary<Guid, TripRevenueSummaryItem> summaryByTrip)
    {
        var routeById = routes.ToDictionary(route => route.RouteId);
        var financialByRoute = currentRows
            .Where(row => row.TripId.HasValue)
            .GroupBy(row => summaryByTrip[row.TripId!.Value].RouteId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var routeIds = routeById.Keys
            .Concat(financialByRoute.Keys)
            .Distinct()
            .ToArray();
        var result = new List<OperatorRoutePerformanceItem>(routeIds.Length);
        foreach (var routeId in routeIds)
        {
            routeById.TryGetValue(routeId, out var route);
            var financialRows = financialByRoute.GetValueOrDefault(routeId) ?? [];
            var financial = Sum(financialRows);
            var summary = financialRows
                .Where(row => row.TripId.HasValue)
                .Select(row => summaryByTrip[row.TripId!.Value])
                .OrderBy(item => item.TripId)
                .FirstOrDefault();
            if (route is null && summary is null)
            {
                throw new UpstreamUnavailableException();
            }

            var tripCount = route?.TripCount ?? 0;
            var completedTripCount = route?.CompletedTripCount ?? 0;
            result.Add(new OperatorRoutePerformanceItem(
                routeId,
                route?.RouteName ?? summary!.RouteName,
                route?.OriginName ?? summary!.OriginName,
                route?.DestinationName ?? summary!.DestinationName,
                tripCount,
                completedTripCount,
                SumCounts(financialRows, row => row.BookingCount),
                SumCounts(financialRows, row => row.ParcelCount),
                financial.Total,
                CompletionRate(tripCount, completedTripCount)));
        }

        return result
            .OrderBy(item => item.RouteName, StringComparer.Ordinal)
            .ThenBy(item => item.RouteId)
            .ToArray();
    }

    private static IReadOnlyDictionary<Guid, TripRevenueSummaryItem> ValidateAndIndexSummaries(
        IReadOnlyList<Guid> tripIds,
        IReadOnlyList<TripRevenueSummaryItem> summaries)
    {
        if (summaries.Count != tripIds.Count
            || summaries.Any(item => item.TripId == Guid.Empty
                || item.RouteId == Guid.Empty
                || string.IsNullOrWhiteSpace(item.RouteName)
                || string.IsNullOrWhiteSpace(item.OriginName)
                || string.IsNullOrWhiteSpace(item.DestinationName))
            || summaries.Select(item => item.TripId).Distinct().Count() != tripIds.Count)
        {
            throw new UpstreamUnavailableException();
        }

        var byTrip = summaries.ToDictionary(item => item.TripId);
        if (tripIds.Any(id => !byTrip.ContainsKey(id)))
        {
            throw new UpstreamUnavailableException();
        }

        return byTrip;
    }

    private static void ValidateRoutes(IReadOnlyList<TripRoutePerformanceItem> routes)
    {
        if (routes.Select(item => item.RouteId).Distinct().Count() != routes.Count
            || routes.Any(item => item.RouteId == Guid.Empty
                || string.IsNullOrWhiteSpace(item.RouteName)
                || string.IsNullOrWhiteSpace(item.OriginName)
                || string.IsNullOrWhiteSpace(item.DestinationName)
                || item.TripCount < 0
                || item.CompletedTripCount < 0
                || item.CompletedTripCount > item.TripCount))
        {
            throw new UpstreamUnavailableException();
        }
    }

    private static (long Ticket, long Parcel, long Total, int TripCount) Sum(
        IReadOnlyList<OperatorRevenueLedgerReadModel> rows)
    {
        long ticket = 0;
        long parcel = 0;
        foreach (var row in rows)
        {
            ticket = checked(ticket + row.NetTicketRevenueVnd);
            parcel = checked(parcel + row.NetParcelRevenueVnd);
        }

        return (
            ticket,
            parcel,
            checked(ticket + parcel),
            rows.Where(row => row.TripId.HasValue).Select(row => row.TripId!.Value).Distinct().Count());
    }

    private static int SumCounts(
        IEnumerable<OperatorRevenueLedgerReadModel> rows,
        Func<OperatorRevenueLedgerReadModel, int> selector)
    {
        var result = 0;
        foreach (var row in rows)
        {
            result = checked(result + selector(row));
        }

        return result;
    }

    private static long Average(long total, int tripCount)
        => tripCount == 0
            ? 0
            : checked((long)Math.Round((decimal)total / tripCount, 0, MidpointRounding.AwayFromZero));

    private static decimal CompletionRate(int tripCount, int completedTripCount)
        => tripCount == 0
            ? 0m
            : Math.Round(
                (decimal)completedTripCount * 100m / tripCount,
                2,
                MidpointRounding.AwayFromZero);
}
