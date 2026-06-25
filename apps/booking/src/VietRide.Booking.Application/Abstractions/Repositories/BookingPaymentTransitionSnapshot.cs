using VietRide.Booking.Application.Abstractions.ServiceClients;

namespace VietRide.Booking.Application.Abstractions.Repositories;

public sealed record BookingPaymentTransitionSnapshot(
    Guid BookingId,
    Guid PassengerUserId,
    Guid TripId,
    Guid? SeatLockToken,
    long TotalAmount,
    Guid? VoucherUsageId,
    IReadOnlyList<PassengerSeatAssignment> PassengerSeatAssignments);
