using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.Domain.Services;

public static class ScheduleChangeResolutionStateMachine
{
    public const string MajorInitialPhase = "MAJOR_INITIAL_PHASE";

    public static DateTimeOffset GetEffectiveCutoff(
        BookingPendingAction action,
        DateTimeOffset initialDeadline,
        DateTimeOffset? terminalDeadline)
    {
        if (action.Reason != BookingPendingActionReason.SCHEDULE_CHANGE || action.Severity is null)
        {
            throw new InvalidOperationException("Pending action is not a resolvable schedule change.");
        }

        if (initialDeadline != action.Deadline)
        {
            throw new InvalidOperationException("Frozen initial deadline does not match the pending action.");
        }

        return action.Severity switch
        {
            BookingPendingActionSeverity.MEDIUM when terminalDeadline is null => initialDeadline,
            BookingPendingActionSeverity.MAJOR when terminalDeadline.HasValue
                && initialDeadline < terminalDeadline.Value => terminalDeadline.Value,
            BookingPendingActionSeverity.MAJOR => initialDeadline,
            _ => throw new InvalidOperationException("Pending action severity metadata is invalid."),
        };
    }

    public static bool IsMajorInitialPhaseDue(
        BookingPendingAction action,
        DateTimeOffset initialDeadline,
        DateTimeOffset? terminalDeadline,
        DateTimeOffset now)
        => action.Reason == BookingPendingActionReason.SCHEDULE_CHANGE
            && action.Severity == BookingPendingActionSeverity.MAJOR
            && terminalDeadline.HasValue
            && initialDeadline < terminalDeadline.Value
            && now > initialDeadline
            && now <= terminalDeadline.Value;

    public static bool IsAutoAcceptDue(
        BookingPendingAction action,
        DateTimeOffset initialDeadline,
        DateTimeOffset? terminalDeadline,
        DateTimeOffset now)
        => now > GetEffectiveCutoff(action, initialDeadline, terminalDeadline);
}
