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
        if (ResolvedAt.HasValue)
        {
            return;
        }

        ResolvedAt = resolvedAt;
        ResolvedAction = resolvedAction;
    }

    /// <summary>
    /// Returns whether the response deadline is strictly in the past.
    /// Equality is still eligible for a passenger response.
    /// </summary>
    public bool IsDeadlineExpired(DateTimeOffset now) => Deadline < now;

    public void ResolveScheduleChange(
        BookingPendingActionResolved resolvedAction,
        DateTimeOffset resolvedAt,
        DateTimeOffset effectiveCutoff)
    {
        if (Reason != BookingPendingActionReason.SCHEDULE_CHANGE
            || ResolvedAt.HasValue
            || resolvedAction is not (BookingPendingActionResolved.ACCEPTED or BookingPendingActionResolved.REJECTED))
        {
            throw new InvalidOperationException("Pending action cannot be resolved as a schedule change.");
        }

        if (resolvedAt > effectiveCutoff)
        {
            throw new InvalidOperationException("Pending action is past its effective cutoff.");
        }

        ResolvedAt = resolvedAt;
        ResolvedAction = resolvedAction;
    }

    public void ResolveRouteChange(
        BookingPendingActionResolved resolvedAction,
        DateTimeOffset resolvedAt)
    {
        if (Reason != BookingPendingActionReason.ROUTE_CHANGE
            || ResolvedAt.HasValue
            || resolvedAction is not (BookingPendingActionResolved.ACCEPTED or BookingPendingActionResolved.REJECTED))
        {
            throw new InvalidOperationException("Pending action cannot be resolved as a route change.");
        }

        if (resolvedAt > Deadline)
        {
            throw new InvalidOperationException("Pending action is past its route-change deadline.");
        }

        ResolvedAt = resolvedAt;
        ResolvedAction = resolvedAction;
    }

    public void ExpireRouteChange(DateTimeOffset resolvedAt)
    {
        if (Reason != BookingPendingActionReason.ROUTE_CHANGE
            || ResolvedAt.HasValue
            || resolvedAt <= Deadline)
        {
            throw new InvalidOperationException("Pending action cannot be expired as a route change.");
        }

        ResolvedAt = resolvedAt;
        ResolvedAction = BookingPendingActionResolved.REJECTED;
    }

    public void AutoAcceptScheduleChange(DateTimeOffset resolvedAt, DateTimeOffset effectiveCutoff)
    {
        if (Reason != BookingPendingActionReason.SCHEDULE_CHANGE
            || Severity is null
            || ResolvedAt.HasValue)
        {
            throw new InvalidOperationException("Pending action cannot be auto-accepted as a schedule change.");
        }

        if (resolvedAt <= effectiveCutoff)
        {
            throw new InvalidOperationException("Pending action has not passed its effective cutoff.");
        }

        ResolvedAt = resolvedAt;
        ResolvedAction = BookingPendingActionResolved.ACCEPTED;
    }
}
