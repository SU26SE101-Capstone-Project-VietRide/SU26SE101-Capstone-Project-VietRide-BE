using System.Globalization;
using MediatR;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.Admin.PlatformReports;
using VietRide.Payment.Application.Features.RevenueAnalytics.Core;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.Application.Features.RevenueAnalytics.Admin;

public sealed class GetAdminRevenueAnalyticsQueryHandler
    : IRequestHandler<GetAdminRevenueAnalyticsQuery, AdminRevenueAnalyticsResponse>
{
    private readonly IRevenueAnalyticsRepository repository;
    private readonly IIdentityOperatorSummaryClient identity;
    private readonly ITripRevenueAnalyticsClient trip;

    public GetAdminRevenueAnalyticsQueryHandler(
        IRevenueAnalyticsRepository repository,
        IIdentityOperatorSummaryClient identity,
        ITripRevenueAnalyticsClient trip)
    {
        this.repository = repository;
        this.identity = identity;
        this.trip = trip;
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
        var currentRows = await repository.GetAdminMonthlyRevenueAsync(
            range.FromUtc,
            range.ToUtc,
            cancellationToken);
        var previousRows = await repository.GetAdminMonthlyRevenueAsync(
            range.PreviousFromUtc,
            range.PreviousToUtc,
            cancellationToken);
        var topRows = await repository.GetTopOperatorPayoutsAsync(
            range.FromUtc,
            range.ToUtc,
            top,
            cancellationToken);

        var current = Sum(currentRows);
        var previous = Sum(previousRows);
        var monthly = BuildMonthly(range.From, range.To, currentRows);
        var topOperators = await EnrichTopOperatorsAsync(topRows, cancellationToken);

        return new AdminRevenueAnalyticsResponse(
            new AdminRevenuePeriod(range.From, range.To, RevenueAnalyticsPeriodRules.Timezone),
            new AdminRevenueSummary(
                RevenueComparisonFactory.Create(current.Gross, previous.Gross),
                RevenueComparisonFactory.Create(current.Platform, previous.Platform),
                RevenueComparisonFactory.Create(current.Paid, previous.Paid)),
            monthly,
            topOperators);
    }

    private async Task<IReadOnlyList<AdminTopOperatorItem>> EnrichTopOperatorsAsync(
        IReadOnlyList<TopOperatorPayoutReadModel> rows,
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
            var platform = row?.PlatformRevenueVnd ?? 0;
            var paid = row?.PaidToOperatorsVnd ?? 0;
            result.Add(new AdminRevenueMonthItem(
                current.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                checked(platform + paid),
                paid,
                platform));
            if (current == last)
            {
                break;
            }

            current = current.AddMonths(1);
        }

        return result;
    }

    private static (long Platform, long Paid, long Gross) Sum(
        IReadOnlyList<AdminRevenueMonthReadModel> rows)
    {
        long platform = 0;
        long paid = 0;
        foreach (var row in rows)
        {
            platform = checked(platform + row.PlatformRevenueVnd);
            paid = checked(paid + row.PaidToOperatorsVnd);
        }

        return (platform, paid, checked(platform + paid));
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
