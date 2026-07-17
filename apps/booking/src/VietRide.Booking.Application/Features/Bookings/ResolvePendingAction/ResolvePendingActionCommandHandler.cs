using System.Text.Json;
using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Domain.Constants;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.Services;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Application.Features.Bookings.ResolvePendingAction;

public sealed class ResolvePendingActionCommandHandler(
    IBookingPendingActionRepository pendingActions,
    IBookingRepository bookings,
    IBookingStatusHistoryRepository statusHistory,
    IIntegrationEventOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<ResolvePendingActionCommand, ResolvePendingActionResult>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<ResolvePendingActionResult> Handle(
        ResolvePendingActionCommand request,
        CancellationToken cancellationToken)
        => unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var now = clock.UtcNow;

            // All resolution paths lock action first and Booking second to avoid lock-order inversions.
            var pendingAction = await pendingActions.GetByIdForUpdateAsync(request.ActionId, cancellationToken);
            var booking = await bookings.FindByIdForUpdateAsync(request.BookingId, cancellationToken);

            if (booking is null || booking.PassengerUserId != request.PassengerUserId)
            {
                throw BookingNotFound();
            }

            if (pendingAction is null)
            {
                throw new CodedNotFoundException(
                    "BOOKING_PENDING_ACTION_NOT_FOUND",
                    "Booking pending action was not found.");
            }

            if (pendingAction.BookingId != booking.Id)
            {
                throw BookingNotFound();
            }

            ThrowIfTerminal(pendingAction);

            if (pendingAction.Reason != BookingPendingActionReason.SCHEDULE_CHANGE
                || pendingAction.Severity is null
                || booking.Status != BookingStatus.CONFIRMED)
            {
                throw NotResolvable();
            }

            var frozen = ParseFrozenMetadata(pendingAction, booking.TotalAmount);
            var effectiveCutoff = GetEffectiveCutoff(pendingAction, frozen.InitialDeadline, frozen.TerminalDeadline);
            if (now > effectiveCutoff)
            {
                throw new CodedConflictException(
                    "BOOKING_PENDING_ACTION_EXPIRED",
                    "Booking pending action has expired.");
            }

            var resolvedAction = Enum.Parse<BookingPendingActionResolved>(request.Action!, ignoreCase: false);
            pendingAction.ResolveScheduleChange(resolvedAction, now, effectiveCutoff);
            pendingActions.Update(pendingAction);

            if (resolvedAction == BookingPendingActionResolved.REJECTED)
            {
                booking.Cancel(BookingCancellationReason.SCHEDULE_CHANGED, now, refundOverride: true);
                bookings.Update(booking);
                await statusHistory.AddAsync(
                    BookingStatusHistory.Create(
                        booking.Id,
                        BookingStatus.CANCELLED,
                        now,
                        BookingStatusHistorySource.CancelBooking,
                        actorUserId: null,
                        BookingCancellationReason.SCHEDULE_CHANGED.ToString()),
                    cancellationToken);

                var eventId = Guid.NewGuid();
                var cancelled = new BookingCancelledIntegrationEvent(
                    eventId,
                    now,
                    booking.Id,
                    booking.BookingCode.Value,
                    booking.PassengerUserId,
                    frozen.RefundAmount.Amount,
                    true,
                    BookingCancellationReason.SCHEDULE_CHANGED.ToString(),
                    booking.Tickets.Select(ticket => ticket.TicketCode.Value).Order(StringComparer.Ordinal).ToArray(),
                    booking.Tickets.Count);
                await outbox.EnqueueAsync(
                    eventId,
                    BookingCancelledIntegrationEvent.EventTypeValue,
                    JsonSerializer.Serialize(cancelled, JsonOptions),
                    cancellationToken);
            }

            return new ResolvePendingActionResult(
                booking.Id,
                pendingAction.Id,
                resolvedAction.ToString(),
                now);
        }, cancellationToken);

    private static DateTimeOffset GetEffectiveCutoff(
        BookingPendingAction action,
        DateTimeOffset initialDeadline,
        DateTimeOffset? terminalDeadline)
    {
        try
        {
            return ScheduleChangeResolutionStateMachine.GetEffectiveCutoff(
                action,
                initialDeadline,
                terminalDeadline);
        }
        catch (InvalidOperationException)
        {
            throw NotResolvable();
        }
    }

    private static (DateTimeOffset InitialDeadline, DateTimeOffset? TerminalDeadline, Money RefundAmount) ParseFrozenMetadata(
        BookingPendingAction action,
        Money bookingTotal)
    {
        try
        {
            using var document = JsonDocument.Parse(action.Metadata ?? string.Empty);
            var root = document.RootElement;
            var expected = new HashSet<string>(StringComparer.Ordinal)
            {
                "sourceEventId", "oldDeparture", "newDeparture", "severity", "initialDeadline",
                "terminalDeadline", "refundBasisAmount", "refundPercent", "refundAmount",
            };
            if (root.ValueKind != JsonValueKind.Object
                || !root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal).SetEquals(expected)
                || root.GetProperty("sourceEventId").GetGuid() == Guid.Empty
                || root.GetProperty("oldDeparture").GetDateTimeOffset() == default
                || root.GetProperty("newDeparture").GetDateTimeOffset() == default)
            {
                throw new InvalidOperationException();
            }

            var severity = root.GetProperty("severity").GetString();
            var initialDeadline = root.GetProperty("initialDeadline").GetDateTimeOffset();
            var terminalElement = root.GetProperty("terminalDeadline");
            var terminalDeadline = terminalElement.ValueKind == JsonValueKind.Null
                ? (DateTimeOffset?)null
                : terminalElement.GetDateTimeOffset();
            var refundBasisAmount = root.GetProperty("refundBasisAmount").GetInt64();
            var refundPercent = root.GetProperty("refundPercent").GetInt32();
            var storedRefundAmount = root.GetProperty("refundAmount").GetInt64();
            var expectedPercent = action.Severity == BookingPendingActionSeverity.MEDIUM ? 50 : 100;
            var calculated = CancellationRefundCalculator.CalculateExplicitPercentRefund(
                Money.FromRaw(refundBasisAmount),
                refundPercent);

            if (!string.Equals(severity, action.Severity.ToString(), StringComparison.Ordinal)
                || refundBasisAmount != bookingTotal.Amount
                || refundPercent != expectedPercent
                || storedRefundAmount != calculated.Amount)
            {
                throw new InvalidOperationException();
            }

            return (
                initialDeadline,
                terminalDeadline,
                calculated);
        }
        catch (Exception exception) when (exception is JsonException
            or InvalidOperationException
            or FormatException
            or ArgumentOutOfRangeException)
        {
            throw NotResolvable();
        }
    }

    private static void ThrowIfTerminal(BookingPendingAction action)
    {
        if (action.ResolvedAction == BookingPendingActionResolved.SUPERSEDED)
        {
            throw new CodedConflictException(
                "BOOKING_PENDING_ACTION_SUPERSEDED",
                "Booking pending action was superseded.");
        }

        if (action.ResolvedAt.HasValue || action.ResolvedAction.HasValue)
        {
            throw new CodedConflictException(
                "BOOKING_PENDING_ACTION_ALREADY_RESOLVED",
                "Booking pending action was already resolved.");
        }
    }

    private static CodedNotFoundException BookingNotFound()
        => new("BOOKING_NOT_FOUND", "Booking not found.");

    private static CodedConflictException NotResolvable()
        => new(
            "BOOKING_PENDING_ACTION_NOT_RESOLVABLE",
            "Booking pending action cannot be resolved by this endpoint.");

}
