using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Domain.Constants;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.Services;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Application.Features.Bookings.CancelBooking;

/// <summary>
/// Handles POST /v1/bookings/{bookingId}/cancel.
/// Two-step refund model: CONFIRMED/PENDING_PAYMENT -> CANCELLED here, then REFUNDED after wallet credit event.
/// </summary>
public sealed class CancelBookingCommandHandler : IRequestHandler<CancelBookingCommand, CancelBookingResult>
{
    private const string EventType = "booking.booking.cancelled";
    private const string RefundMethod = "WALLET";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IBookingRepository _bookings;
    private readonly IBookingStatusHistoryRepository _statusHistory;
    private readonly ITripServiceClient _tripClient;
    private readonly IOperatorServiceClient _operatorClient;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;
    private readonly ILogger<CancelBookingCommandHandler> _logger;
    private readonly IBookingPendingActionRepository _pendingActions;

    public CancelBookingCommandHandler(
        IBookingRepository bookings,
        ITripServiceClient tripClient,
        IOperatorServiceClient operatorClient,
        IIntegrationEventOutbox outbox,
        IClock clock,
        ILogger<CancelBookingCommandHandler> logger,
        IBookingStatusHistoryRepository statusHistory,
        IBookingPendingActionRepository pendingActions)
    {
        _bookings = bookings;
        _statusHistory = statusHistory;
        _tripClient = tripClient;
        _operatorClient = operatorClient;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
        _pendingActions = pendingActions;
    }

    public async Task<CancelBookingResult> Handle(
        CancelBookingCommand request,
        CancellationToken cancellationToken)
    {
        await _bookings.AcquirePaymentTransitionLocksAsync(
            [request.BookingId],
            cancellationToken).ConfigureAwait(false);

        var booking = await _bookings.FindByIdWithPassengersAsync(request.BookingId, cancellationToken);
        if (booking is null)
        {
            throw new CodedNotFoundException(
                "BOOKING_NOT_FOUND",
                $"Booking '{request.BookingId}' not found.");
        }

        if (booking.PassengerUserId != request.PassengerUserId)
        {
            throw new ForbiddenException(
                "FORBIDDEN",
                "Only the booking owner may cancel the booking.");
        }

        if (booking.Status is not (BookingStatus.CONFIRMED or BookingStatus.PENDING_PAYMENT))
        {
            throw new ConflictException(
                "BOOKING_NOT_CANCELLABLE",
                "Only CONFIRMED or PENDING_PAYMENT bookings may be cancelled.");
        }

        var trip = await _tripClient.GetTripSnapshotAsync(booking.TripId, cancellationToken);
        if (trip is null)
        {
            throw new CodedNotFoundException(
                "TRIP_NOT_FOUND",
                $"Trip '{booking.TripId}' not found.");
        }

        if (!IsTripCancellable(trip.Status))
        {
            throw new ConflictException(
                "BOOKING_NOT_CANCELLABLE",
                "Trip status does not allow booking cancellation.");
        }

        var now = _clock.UtcNow;
        var reason = Enum.Parse<BookingCancellationReason>(request.Reason, ignoreCase: false);
        var refundOverride = reason == BookingCancellationReason.STOP_DISABLED_REFUSED;
        BookingPendingAction? stopDisabledAction = null;
        if (reason == BookingCancellationReason.STOP_DISABLED_REFUSED)
        {
            stopDisabledAction = await _pendingActions.GetActiveByBookingIdForUpdateAsync(booking.Id, cancellationToken);
            if (stopDisabledAction is null)
                throw new ConflictException("BOOKING_PENDING_ACTION_NOT_RESOLVABLE", "No active STOP_DISABLED action exists.");
            if (stopDisabledAction is null || stopDisabledAction.Reason != BookingPendingActionReason.STOP_DISABLED)
                throw new ConflictException("BOOKING_PENDING_ACTION_NOT_RESOLVABLE", "No active STOP_DISABLED action exists.");
            if (stopDisabledAction.Deadline < now)
                throw new ConflictException("BOOKING_PENDING_ACTION_EXPIRED", "Booking pending action has expired.");
        }
        var operatorLookup = await _operatorClient.GetOperatorAsync(booking.OperatorId, cancellationToken);
        var policy = ParseCancellationPolicy(operatorLookup?.CancellationPolicy);
        var paidAmount = booking.Status == BookingStatus.PENDING_PAYMENT
            ? Money.Zero
            : booking.TotalAmount;
        var hoursToDeparture = (decimal)(trip.DepartureDateTime - now).TotalHours;
        var refundAmount = CancellationRefundCalculator.CalculateRefundAmount(
            paidAmount,
            hoursToDeparture,
            policy,
            refundOverride);

        var previousStatus = booking.Status;
        var cancelled = await _bookings.TryCancelAsync(
            booking.Id,
            reason,
            now,
            refundOverride,
            cancellationToken);
        if (!cancelled)
        {
            throw new ConflictException(
                "BOOKING_NOT_CANCELLABLE",
                "Only CONFIRMED or PENDING_PAYMENT bookings may be cancelled.");
        }

        if (stopDisabledAction is not null)
        {
            stopDisabledAction.Resolve(BookingPendingActionResolved.REJECTED, now);
            _pendingActions.Update(stopDisabledAction);
        }

        await _statusHistory.AddAsync(
            BookingStatusHistory.Create(
                booking.Id,
                BookingStatus.CANCELLED,
                now,
                BookingStatusHistorySource.CancelBooking,
                request.PassengerUserId,
                reason.ToString()),
            cancellationToken);

        if (booking.SeatLockToken.HasValue)
        {
            await _tripClient.ReleaseSeatsAsync(
                booking.TripId,
                booking.SeatLockToken.Value,
                booking.Passengers.Select(p => p.SeatNumber).OfType<string>().ToArray(),
                cancellationToken);
        }
        else
        {
            _logger.LogWarning(
                "Booking {BookingId} has no seat lock token; skipping seat release during cancellation.",
                booking.Id);
        }

        var eventId = Guid.NewGuid();
        var cancelledEvent = new Events.BookingCancelledIntegrationEvent(
            eventId,
            now,
            booking.Id,
            booking.BookingCode.Value,
            booking.PassengerUserId,
            refundAmount.Amount,
            refundOverride,
            reason.ToString(),
            booking.Tickets.Select(ticket => ticket.TicketCode.Value).ToArray(),
            booking.Tickets.Count,
            booking.TripId,
            previousStatus.ToString(),
            booking.Passengers.Select(passenger => passenger.SeatNumber).OfType<string>().ToArray());

        await _outbox.EnqueueAsync(
            eventId,
            EventType,
            JsonSerializer.Serialize(cancelledEvent, JsonOptions),
            cancellationToken);

        return new CancelBookingResult(
            BookingId: booking.Id,
            Status: BookingStatus.CANCELLED.ToString(),
            RefundAmount: refundAmount.Amount,
            RefundMethod: RefundMethod);
    }

    // A passenger may cancel while the trip has not yet departed.
    // technical_context_v7 6.2: cancellable window is SCHEDULED or BOARDING (line 2050);
    // cancellation is blocked only once the trip is IN_PROGRESS or COMPLETED (line 2166).
    private static bool IsTripCancellable(string status)
        => string.Equals(status, "SCHEDULED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "BOARDING", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<CancellationPolicyTier>? ParseCancellationPolicy(JsonElement? policy)
    {
        if (!policy.HasValue || policy.Value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var tiers = new List<CancellationPolicyTier>();
        foreach (var tier in policy.Value.EnumerateArray())
        {
            if (!tier.TryGetProperty("hoursBeforeDeparture", out var hours)
                || !tier.TryGetProperty("feePercent", out var feePercent))
            {
                continue;
            }

            tiers.Add(new CancellationPolicyTier(
                hours.GetInt32(),
                feePercent.GetDecimal()));
        }

        return tiers;
    }
}
