namespace VietRide.Trip.Application.Abstractions.SeatLock;

public sealed record SeatLockIdempotencyReservation(
    bool Reserved,
    string? ReservationToken,
    SeatLockIdempotencyEntry? ExistingEntry);
