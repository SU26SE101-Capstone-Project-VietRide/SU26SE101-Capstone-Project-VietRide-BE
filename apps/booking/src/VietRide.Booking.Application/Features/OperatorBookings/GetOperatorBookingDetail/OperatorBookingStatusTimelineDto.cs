namespace VietRide.Booking.Application.Features.OperatorBookings.GetOperatorBookingDetail;

public sealed record OperatorBookingStatusTimelineDto(string Status, DateTimeOffset OccurredAt, string? ReasonCode);
