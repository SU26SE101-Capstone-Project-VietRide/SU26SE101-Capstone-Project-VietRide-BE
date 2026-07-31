using VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;

namespace VietRide.Booking.Application.Features.OperatorBookings.GetOperatorBookingDetail;

public sealed record OperatorBookingDetailDto(
    Guid Id, string BookingCode, Guid BuyerUserId, Guid TripId, string Status, OperatorBookingTripDto Trip,
    int SeatCount, long BaseFare, long DiscountAmount, long TotalAmount,
    Guid? PickupStationId, Guid? PickupStopId, Guid? DropoffStationId, Guid? DropoffStopId,
    Guid? BookingGroupId, string? TripDirection, string? CancellationReason, DateTimeOffset CreatedAt,
    IReadOnlyList<OperatorBookingSeatDto> Seats, IReadOnlyList<OperatorBookingStatusTimelineDto> StatusTimeline,
    OperatorBookingBuyerDto? Buyer = null);
