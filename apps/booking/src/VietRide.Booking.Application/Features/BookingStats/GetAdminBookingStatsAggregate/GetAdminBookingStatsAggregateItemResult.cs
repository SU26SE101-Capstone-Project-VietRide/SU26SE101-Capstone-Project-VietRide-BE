using System.Text.Json.Serialization;

namespace VietRide.Booking.Application.Features.BookingStats.GetAdminBookingStatsAggregate;

public sealed record GetAdminBookingStatsAggregateItemResult(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? OperatorId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OperatorName,
    DateOnly? Date,
    int TotalBookings,
    long TotalRevenue,
    int TotalCancellations,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TotalNoShows,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TotalPartialNoShows,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TotalCompleted);
