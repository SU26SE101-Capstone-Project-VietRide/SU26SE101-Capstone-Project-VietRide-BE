using System.Text.Json.Serialization;

namespace VietRide.Booking.Application.Features.BookingStats.GetOperatorBookingStats;

public sealed record GetOperatorBookingStatsItemResult(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? OperatorId,
    DateOnly Date,
    int TotalBookings,
    long TotalRevenue,
    int TotalCancellations,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TotalNoShows,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TotalPartialNoShows,
    int TotalCompleted);
