using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.BookingStats;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.BookingStats.GetOperatorBookingStats;

public sealed class GetOperatorBookingStatsQueryHandler
    : IRequestHandler<GetOperatorBookingStatsQuery, GetOperatorBookingStatsResult>
{
    private static readonly HashSet<string> SupportedGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        BookingStatsQueryRules.DateGroup,
        BookingStatsQueryRules.MonthGroup,
    };

    private readonly IBookingStatsRepository _stats;

    public GetOperatorBookingStatsQueryHandler(IBookingStatsRepository stats)
    {
        _stats = stats;
    }

    public async Task<GetOperatorBookingStatsResult> Handle(
        GetOperatorBookingStatsQuery request,
        CancellationToken cancellationToken)
    {
        if (!SupportedGroups.Contains(request.GroupBy))
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Operator booking stats supports groupBy=date or groupBy=month.",
                [new ValidationError("groupBy", "Only 'date' or 'month' is supported.")]);
        }

        var groupBy = request.GroupBy.ToLowerInvariant();
        var isMonth = groupBy == BookingStatsQueryRules.MonthGroup;
        BookingStatsQueryRules.ValidateRange(request.From, request.To, requireCompleteRange: isMonth);

        var rows = await _stats.GetOperatorStatsAsync(
            request.OperatorId,
            request.From,
            request.To,
            groupBy,
            cancellationToken);

        var items = isMonth
            ? BuildMonthlyItems(request, rows)
            : rows.Select(MapItem).ToList();

        return new GetOperatorBookingStatsResult(
            items,
            items.Sum(item => item.TotalBookings));
    }

    private static IReadOnlyList<GetOperatorBookingStatsItemResult> BuildMonthlyItems(
        GetOperatorBookingStatsQuery request,
        IReadOnlyList<OperatorBookingStatsReadModel> rows)
    {
        var byMonth = rows
            .GroupBy(row => new DateOnly(row.Date.Year, row.Date.Month, 1))
            .ToDictionary(
                group => group.Key,
                group => new GetOperatorBookingStatsItemResult(
                    OperatorId: null,
                    group.Key,
                    group.Sum(row => row.TotalBookings),
                    group.Sum(row => row.TotalCancellations),
                    TotalNoShows: null,
                    TotalPartialNoShows: null,
                    group.Sum(row => row.TotalCompleted)));

        return BookingStatsQueryRules
            .EnumerateMonthStarts(request.From!.Value, request.To!.Value)
            .Select(month => byMonth.GetValueOrDefault(month) ?? new GetOperatorBookingStatsItemResult(
                OperatorId: null,
                month,
                TotalBookings: 0,
                TotalCancellations: 0,
                TotalNoShows: null,
                TotalPartialNoShows: null,
                TotalCompleted: 0))
            .ToList();
    }

    private static GetOperatorBookingStatsItemResult MapItem(OperatorBookingStatsReadModel row)
        => new(
            row.OperatorId,
            row.Date,
            row.TotalBookings,
            row.TotalCancellations,
            row.TotalNoShows,
            TotalPartialNoShows: 0,
            row.TotalCompleted);
}
