namespace VietRide.Booking.Application.Features.OperatorBookings.BuyerSnapshots;

public sealed record BookingBuyerSnapshotCandidate(
    Guid BookingId,
    Guid BuyerUserId);
