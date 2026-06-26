using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.BookingStats.GetAdminBookingStatsAggregate;

public sealed class GetAdminBookingStatsAggregateQueryHandler
    : IRequestHandler<GetAdminBookingStatsAggregateQuery, GetAdminBookingStatsAggregateResult>
{
    private static readonly HashSet<string> SupportedGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        "operator",
        "date",
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
                "Admin booking stats aggregate supports groupBy=operator or groupBy=date.",
                [new ValidationError("groupBy", "Only 'operator' or 'date' is supported.")]);
        }

        var rows = await _stats.GetAdminAggregateStatsAsync(
            request.From,
            request.To,
            request.GroupBy,
            cancellationToken);

        return new GetAdminBookingStatsAggregateResult(
            rows.Select(row => new GetAdminBookingStatsAggregateItemResult(
                    row.OperatorId,
                    row.OperatorName,
                    row.Date,
                    row.TotalBookings,
                    row.TotalRevenue,
                    row.TotalCancellations,
                    row.TotalNoShows,
                    TotalPartialNoShows: 0,
                    row.TotalCompleted))
                .ToList());
    }
}
