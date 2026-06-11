using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Domain.Entities;

/// <summary>
/// A pending action that requires a passenger response (e.g. ROUTE_CHANGE, SCHEDULE_CHANGE).
/// Only one active (unresolved) action is allowed per booking at a time
/// — enforced by partial unique index uq_booking_pending_actions_active_per_booking.
/// </summary>
public sealed class BookingPendingAction : BaseEntity<Guid>
{
    public Guid BookingId { get; private set; }
    public BookingPendingActionReason Reason { get; private set; }
    public BookingPendingActionSeverity? Severity { get; private set; }
    public DateTimeOffset Deadline { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public BookingPendingActionResolved? ResolvedAction { get; private set; }

    /// <summary>JSONB metadata — schema varies by reason; validated at the Application layer.</summary>
    public string? Metadata { get; private set; }

    // Navigation (EF)
    public Booking? Booking { get; private set; }

    private BookingPendingAction() { }

    public static BookingPendingAction Create(
        Guid bookingId,
        BookingPendingActionReason reason,
        DateTimeOffset deadline,
        BookingPendingActionSeverity? severity = null,
        string? metadata = null)
    {
        return new BookingPendingAction
        {
            Id = Guid.NewGuid(),
            BookingId = bookingId,
            Reason = reason,
            Severity = severity,
            Deadline = deadline,
            Metadata = metadata,
        };
    }

    public void Resolve(BookingPendingActionResolved resolvedAction, DateTimeOffset resolvedAt)
    {
        ResolvedAt = resolvedAt;
        ResolvedAction = resolvedAction;
    }
}
