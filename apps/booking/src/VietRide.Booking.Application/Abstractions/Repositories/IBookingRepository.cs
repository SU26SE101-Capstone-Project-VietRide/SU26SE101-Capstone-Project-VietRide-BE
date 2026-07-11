using VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Repositories;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.Application.Abstractions.Repositories;

/// <summary>
/// Repository contract for the Booking aggregate.
/// Extends <see cref="IRepository{TEntity,TId}"/> with Booking-specific queries.
/// </summary>
public interface IBookingRepository : IRepository<BookingEntity, Guid>
{
    Task<OperatorBookingListPage> ListOperatorBookingsAsync(
        OperatorBookingListCriteria criteria,
        CancellationToken ct = default)
        => throw new NotSupportedException("Operator booking listing is not implemented by this repository.");

    /// <summary>
    /// Finds a booking by its booking code string (unique index).
    /// </summary>
    Task<BookingEntity?> FindByBookingCodeAsync(string bookingCode, CancellationToken ct = default);

    /// <summary>
    /// Finds a booking containing the ticket code and eagerly loads passengers + tickets.
    /// </summary>
    Task<BookingEntity?> FindByTicketCodeWithPassengersAsync(string ticketCode, CancellationToken ct = default);

    /// <summary>
    /// Finds a booking by id using the aggregate-specific seam.
    /// </summary>
    Task<BookingEntity?> FindByIdAsync(Guid bookingId, CancellationToken ct = default);

    /// <summary>
    /// Returns a booking with Passengers and Tickets eagerly loaded.
    /// Used for saga compensation checks and cancellation.
    /// </summary>
    Task<BookingEntity?> FindByIdWithPassengersAsync(Guid bookingId, CancellationToken ct = default);

    /// <summary>
    /// Returns the data needed to replay the Trip seat-lock seam for a payment event.
    /// Null means the booking is no longer PENDING_PAYMENT and the event is an idempotent no-op.
    /// </summary>
    Task<BookingPaymentTransitionSnapshot?> GetPendingPaymentTransitionSnapshotAsync(
        Guid bookingId,
        CancellationToken ct = default);

    /// <summary>
    /// Status-guarded PENDING_PAYMENT -> CONFIRMED transition.
    /// Returns true only when this call changed the row.
    /// </summary>
    Task<bool> TryConfirmPendingPaymentAsync(
        Guid bookingId,
        DateTimeOffset confirmedAt,
        CancellationToken ct = default);

    /// <summary>
    /// Status-guarded PENDING_PAYMENT -> EXPIRED transition.
    /// Returns true only when this call changed the row.
    /// </summary>
    Task<bool> TryExpirePendingPaymentAsync(
        Guid bookingId,
        DateTimeOffset expiredAt,
        CancellationToken ct = default);

    /// <summary>
    /// Status-guarded CONFIRMED/PENDING_PAYMENT -> CANCELLED transition.
    /// Returns true only when this call changed the row.
    /// </summary>
    Task<bool> TryCancelAsync(
        Guid bookingId,
        BookingCancellationReason reason,
        DateTimeOffset cancelledAt,
        bool refundOverride,
        CancellationToken ct = default);

    /// <summary>
    /// Status-guarded CANCELLED -> REFUNDED transition.
    /// Returns true only when this call changed the row.
    /// </summary>
    Task<bool> TryMarkCancelledRefundedAsync(
        Guid bookingId,
        DateTimeOffset refundedAt,
        CancellationToken ct = default);

    Task<bool> HasConfirmedBookingAsync(Guid passengerUserId, CancellationToken ct = default);
}
