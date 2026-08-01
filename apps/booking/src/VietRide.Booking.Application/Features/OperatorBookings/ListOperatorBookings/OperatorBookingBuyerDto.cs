namespace VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;

public sealed record OperatorBookingBuyerDto(
    Guid UserId,
    string DisplayName,
    string? Phone,
    string? Email,
    string? AvatarUrl);
