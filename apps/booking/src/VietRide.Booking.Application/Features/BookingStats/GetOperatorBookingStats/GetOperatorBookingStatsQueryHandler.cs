using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.BookingStats.GetOperatorBookingStats;

public sealed class GetOperatorBookingStatsQueryHandler
    : IRequestHandler<GetOperatorBookingStatsQuery, GetOperatorBookingStatsResult>
{
    private const string DateGroup = "date";
    private readonly IBookingStatsRepository _stats;

    public GetOperatorBookingStatsQueryHandler(IBookingStatsRepository stats)
    {
        _stats = stats;
    }

    public async Task<GetOperatorBookingStatsResult> Handle(
        GetOperatorBookingStatsQuery request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.GroupBy, DateGroup, StringComparison.OrdinalIgnoreCase))
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Operator booking stats only supports groupBy=date.",
                [new ValidationError("groupBy", "Only 'date' is supported.")]);
        }

        var rows = await _stats.GetOperatorStatsAsync(
            request.OperatorId,
            request.From,
            request.To,
            cancellationToken);

        return new GetOperatorBookingStatsResult(
            rows.Select(row => new GetOperatorBookingStatsItemResult(
                    row.OperatorId,
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
