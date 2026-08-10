using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.BookingStats.GetAdminBookingStatsAggregate;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Time;

namespace VietRide.Booking.Application.Features.Admin.Dashboard;

public sealed class GetAdminDashboardSummaryQueryHandler
    : IRequestHandler<GetAdminDashboardSummaryQuery, AdminDashboardSummaryResponse>
{
    private const int MaximumInclusiveDays = 366;
    private const string BusinessTimeZoneId = BusinessTime.TimeZoneId;

    private readonly IBookingStatsRepository _stats;
    private readonly IIdentityDashboardMetricsClient _identity;
    private readonly IPaymentRevenueSummaryClient _payment;

    public GetAdminDashboardSummaryQueryHandler(
        IBookingStatsRepository stats,
        IIdentityDashboardMetricsClient identity,
        IPaymentRevenueSummaryClient payment)
    {
        _stats = stats;
        _identity = identity;
        _payment = payment;
    }

    public async Task<AdminDashboardSummaryResponse> Handle(
        GetAdminDashboardSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var range = ValidateAndBuildRange(request.From, request.To);
        var currentStats = await _stats.GetAdminAggregateStatsAsync(
            range.CurrentFrom,
            range.CurrentTo,
            "operator",
            cancellationToken);
        var previousStats = await _stats.GetAdminAggregateStatsAsync(
            range.PreviousFrom,
            range.PreviousTo,
            "operator",
            cancellationToken);

        var currentIdentityTask = _identity.GetAsync(
            range.CurrentFrom,
            range.CurrentTo,
            cancellationToken);
        var previousIdentityTask = _identity.GetAsync(
            range.PreviousFrom,
            range.PreviousTo,
            cancellationToken);
        var currentRevenueTask = _payment.GetAsync(
            range.CurrentFrom,
            range.CurrentTo,
            cancellationToken);
        var previousRevenueTask = _payment.GetAsync(
            range.PreviousFrom,
            range.PreviousTo,
            cancellationToken);
        await Task.WhenAll(
            currentIdentityTask,
            previousIdentityTask,
            currentRevenueTask,
            previousRevenueTask);
        var currentIdentity = await currentIdentityTask;
        var previousIdentity = await previousIdentityTask;
        var currentRevenue = await currentRevenueTask;
        var previousRevenue = await previousRevenueTask;

        var approvedOperatorIds = currentIdentity.ApprovedActiveOperatorIds.ToHashSet();
        var currentBookings = currentStats.Sum(row => (long)row.TotalBookings);
        var previousBookings = previousStats.Sum(row => (long)row.TotalBookings);
        var currentActiveOperators = CountActiveOperators(currentStats, approvedOperatorIds);
        var previousActiveOperators = CountActiveOperators(previousStats, approvedOperatorIds);

        var statusTotal = currentIdentity.OperatorStatusCounts.Sum(item => item.Count);
        var statusDistribution = currentIdentity.OperatorStatusCounts
            .Select(item => new AdminDashboardOperatorStatusDistributionResponse(
                item.Status,
                item.Count,
                CalculatePercent(item.Count, statusTotal)))
            .ToArray();

        return new AdminDashboardSummaryResponse(
            new AdminDashboardPeriodResponse(range.CurrentFrom, range.CurrentTo, BusinessTimeZoneId),
            Compare(currentRevenue.TotalProjectRevenueVnd, previousRevenue.TotalProjectRevenueVnd),
            Compare(currentRevenue.NetTransportRevenueVnd, previousRevenue.NetTransportRevenueVnd),
            Compare(currentRevenue.NetTicketRevenueVnd, previousRevenue.NetTicketRevenueVnd),
            Compare(currentRevenue.NetParcelRevenueVnd, previousRevenue.NetParcelRevenueVnd),
            Compare(currentRevenue.SubscriptionRevenueVnd, previousRevenue.SubscriptionRevenueVnd),
            Compare(currentActiveOperators, previousActiveOperators),
            Compare(currentIdentity.ActiveUserCount, previousIdentity.ActiveUserCount),
            Compare(currentBookings, previousBookings),
            currentIdentity.UserRoleCounts
                .Select(item => new AdminDashboardUserDistributionResponse(item.Role, item.Count))
                .ToArray(),
            statusDistribution);
    }

    private static AdminDashboardDateRange ValidateAndBuildRange(DateOnly? from, DateOnly? to)
    {
        if (!from.HasValue || !to.HasValue)
        {
            var errors = new List<ValidationError>();
            if (!from.HasValue)
            {
                errors.Add(new ValidationError("from", "from is required."));
            }
            if (!to.HasValue)
            {
                errors.Add(new ValidationError("to", "to is required."));
            }

            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "from and to are required for the Admin dashboard.",
                errors);
        }

        if (from.Value > to.Value)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "from must be on or before to.",
                [new ValidationError("from", "from must be on or before to.")]);
        }

        var inclusiveDays = to.Value.DayNumber - from.Value.DayNumber + 1;
        if (inclusiveDays > MaximumInclusiveDays)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Admin dashboard range cannot exceed 366 inclusive days.",
                [new ValidationError("to", "The inclusive date range cannot exceed 366 days.")]);
        }

        if (from.Value.DayNumber < inclusiveDays)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "The preceding comparison period is outside the supported calendar.",
                [new ValidationError("from", "from is too early to build an equal comparison period.")]);
        }

        var previousTo = from.Value.AddDays(-1);
        var previousFrom = DateOnly.FromDayNumber(from.Value.DayNumber - inclusiveDays);
        return new AdminDashboardDateRange(
            from.Value,
            to.Value,
            previousFrom,
            previousTo);
    }

    private static long CountActiveOperators(
        IReadOnlyList<AdminBookingStatsAggregateReadModel> rows,
        IReadOnlySet<Guid> approvedOperatorIds)
        => rows
            .Where(row => row.OperatorId.HasValue
                && row.TotalBookings > 0
                && approvedOperatorIds.Contains(row.OperatorId.Value))
            .Select(row => row.OperatorId!.Value)
            .Distinct()
            .LongCount();

    private static AdminDashboardComparisonResponse Compare(long current, long previous)
    {
        var trend = current == previous ? "FLAT" : current > previous ? "UP" : "DOWN";
        decimal? changePercent = previous == 0
            ? current == 0 ? 0m : null
            : Math.Round(
                ((decimal)current - previous) * 100m / Math.Abs((decimal)previous),
                2,
                MidpointRounding.AwayFromZero);
        return new AdminDashboardComparisonResponse(current, previous, changePercent, trend);
    }

    private static decimal CalculatePercent(long value, long total)
        => total == 0
            ? 0m
            : Math.Round((decimal)value * 100m / total, 2, MidpointRounding.AwayFromZero);

    private sealed record AdminDashboardDateRange(
        DateOnly CurrentFrom,
        DateOnly CurrentTo,
        DateOnly PreviousFrom,
        DateOnly PreviousTo);
}
