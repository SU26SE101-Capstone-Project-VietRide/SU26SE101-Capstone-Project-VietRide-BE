using System.Text.Json.Serialization;

namespace VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;

public sealed record OperatorBookingListItem(
    Guid Id,
    string BookingCode,
    Guid TripId,
    string Status,
    OperatorBookingTripDto Trip,
    int SeatCount,
    long TotalAmount,
    DateTimeOffset CreatedAt,
    OperatorBookingBuyerDto? Buyer = null,
    [property: JsonIgnore] Guid BuyerUserId = default);
