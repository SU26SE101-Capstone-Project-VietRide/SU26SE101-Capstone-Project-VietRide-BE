using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.BookingStats;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.BookingStats.GetAdminBookingStatsAggregate;

public sealed class GetAdminBookingStatsAggregateQueryHandler
    : IRequestHandler<GetAdminBookingStatsAggregateQuery, GetAdminBookingStatsAggregateResult>
{
    private static readonly HashSet<string> SupportedGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        BookingStatsQueryRules.OperatorGroup,
        BookingStatsQueryRules.DateGroup,
        BookingStatsQueryRules.MonthGroup,
    };

    private readonly IBookingStatsRepository _stats;

    public GetAdminBookingStatsAggregateQueryHandler(IBookingStatsRepository stats)
    {
        _stats = stats;
    }

    public async Task<GetAdminBookingStatsAggregateResult> Handle(
        GetAdminBookingStatsAggregateQuery request,
        CancellationToken cancellationToken)
    {
        if (!SupportedGroups.Contains(request.GroupBy))
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Admin booking stats aggregate supports groupBy=operator, groupBy=date or groupBy=month.",
                [new ValidationError("groupBy", "Only 'operator', 'date' or 'month' is supported.")]);
        }

        var groupBy = request.GroupBy.ToLowerInvariant();
        var isMonth = groupBy == BookingStatsQueryRules.MonthGroup;
        BookingStatsQueryRules.ValidateRange(request.From, request.To, requireCompleteRange: isMonth);

        var rows = await _stats.GetAdminAggregateStatsAsync(
            request.From,
            request.To,
            groupBy,
            cancellationToken);

        var items = isMonth
            ? BuildMonthlyItems(request, rows)
            : rows.Select(MapItem).ToList();

        return new GetAdminBookingStatsAggregateResult(
            items,
            items.Sum(item => item.TotalBookings),
            items.Sum(item => item.TotalRevenue));
    }

    private static IReadOnlyList<GetAdminBookingStatsAggregateItemResult> BuildMonthlyItems(
        GetAdminBookingStatsAggregateQuery request,
        IReadOnlyList<AdminBookingStatsAggregateReadModel> rows)
    {
        var byMonth = rows
            .Where(row => row.Date.HasValue)
            .GroupBy(row => new DateOnly(row.Date!.Value.Year, row.Date.Value.Month, 1))
            .ToDictionary(
                group => group.Key,
                group => new GetAdminBookingStatsAggregateItemResult(
                    OperatorId: null,
                    OperatorName: null,
                    group.Key,
                    group.Sum(row => row.TotalBookings),
                    group.Sum(row => row.TotalRevenue),
                    group.Sum(row => row.TotalCancellations),
                    TotalNoShows: null,
                    TotalPartialNoShows: null,
                    TotalCompleted: null));

        return BookingStatsQueryRules
            .EnumerateMonthStarts(request.From!.Value, request.To!.Value)
            .Select(month => byMonth.GetValueOrDefault(month) ?? new GetAdminBookingStatsAggregateItemResult(
                OperatorId: null,
                OperatorName: null,
                month,
                TotalBookings: 0,
                TotalRevenue: 0,
                TotalCancellations: 0,
                TotalNoShows: null,
                TotalPartialNoShows: null,
                TotalCompleted: null))
            .ToList();
    }

    private static GetAdminBookingStatsAggregateItemResult MapItem(AdminBookingStatsAggregateReadModel row)
        => new(
            row.OperatorId,
            row.OperatorName,
            row.Date,
            row.TotalBookings,
            row.TotalRevenue,
            row.TotalCancellations,
            row.TotalNoShows,
            TotalPartialNoShows: 0,
            row.TotalCompleted);
}
