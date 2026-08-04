using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Domain.Constants;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Application.Features.Bookings.ConfirmBookingOnPayment;

public sealed class ConfirmBookingOnPaymentCommandHandler
    : IRequestHandler<ConfirmBookingOnPaymentCommand, bool>
{
    private const string BookingReferenceType = "BOOKING";
    private const string BookingGroupReferenceType = "BOOKING_GROUP";
    private const string VnPayMethod = "VNPAY";
    private const string BookingConfirmedEventType = "booking.booking.confirmed";
    private const string LateCaptureReason = "PAYMENT_CAPTURE_AFTER_BOOKING_EXPIRY";
    private const string SeatConfirmationFailedReason = "SEAT_CONFIRMATION_FAILED";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IBookingRepository _bookings;
    private readonly IBookingStatusHistoryRepository _statusHistory;
    private readonly ITripServiceClient _tripClient;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;
    private readonly ILogger<ConfirmBookingOnPaymentCommandHandler> _logger;
    private readonly IBookingService? _bookingService;
    private readonly IVoucherService? _voucherService;

    public ConfirmBookingOnPaymentCommandHandler(
        IBookingRepository bookings,
        ITripServiceClient tripClient,
        IIntegrationEventOutbox outbox,
        IClock clock,
        ILogger<ConfirmBookingOnPaymentCommandHandler> logger,
        IBookingStatusHistoryRepository statusHistory,
        IBookingService? bookingService = null,
        IVoucherService? voucherService = null)
    {
        _bookings = bookings;
        _statusHistory = statusHistory;
        _tripClient = tripClient;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
        _bookingService = bookingService;
        _voucherService = voucherService;
    }

    public async Task<bool> Handle(ConfirmBookingOnPaymentCommand request, CancellationToken cancellationToken)
    {
        if (string.Equals(request.ReferenceType, BookingGroupReferenceType, StringComparison.OrdinalIgnoreCase))
        {
            return await HandleGroupAsync(request, cancellationToken).ConfigureAwait(false);
        }

        if (!string.Equals(request.ReferenceType, BookingReferenceType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        await _bookings.AcquirePaymentTransitionLocksAsync(
            [request.ReferenceId],
            cancellationToken).ConfigureAwait(false);

        var allocation = _bookings.QueryNoTracking()
            .Where(booking => booking.Id == request.ReferenceId)
            .Select(booking => new BookingAllocation(
                booking.Id,
                booking.PassengerUserId,
                booking.Status,
                booking.TotalAmount.Amount))
            .SingleOrDefault();
        if (allocation is null)
        {
            return false;
        }

        EnsureExactPaymentAmount(request.Amount, [allocation]);
        if (allocation.Status == BookingStatus.EXPIRED || IsLateCapture(request))
        {
            return await ExpireAndRequestRefundAsync(
                request,
                [allocation],
                IsLateCapture(request) ? LateCaptureReason : SeatConfirmationFailedReason,
                cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(request.Method, VnPayMethod, StringComparison.OrdinalIgnoreCase)
            && allocation.Status != BookingStatus.PENDING_PAYMENT)
        {
            return await ExpireAndRequestRefundAsync(
                request,
                [allocation],
                SeatConfirmationFailedReason,
                cancellationToken).ConfigureAwait(false);
        }

        if (allocation.Status == BookingStatus.CONFIRMED)
        {
            return false;
        }

        if (allocation.Status != BookingStatus.PENDING_PAYMENT)
        {
            return false;
        }

        var snapshot = await _bookings.GetPendingPaymentTransitionSnapshotAsync(
            allocation.BookingId,
            cancellationToken).ConfigureAwait(false);
        if (snapshot is null || !snapshot.SeatLockToken.HasValue)
        {
            return await ExpireAndRequestRefundAsync(
                request,
                [allocation],
                SeatConfirmationFailedReason,
                cancellationToken).ConfigureAwait(false);
        }

        var outcome = await _tripClient.ConfirmBookedSeatsAsync(
            snapshot.TripId,
            snapshot.SeatLockToken.Value,
            snapshot.BookingId,
            snapshot.PassengerSeatAssignments,
            cancellationToken).ConfigureAwait(false);
        switch (outcome)
        {
            case SeatConfirmationOutcome.Success:
                var transitioned = await ConfirmPersistedBookingAsync(
                    request.PaymentId,
                    snapshot,
                    cancellationToken).ConfigureAwait(false);
                if (transitioned
                    || !string.Equals(request.Method, VnPayMethod, StringComparison.OrdinalIgnoreCase))
                {
                    return transitioned;
                }

                return await CompensateConfirmationCasLossAsync(
                    request,
                    [snapshot.BookingId],
                    cancellationToken).ConfigureAwait(false);

            case SeatConfirmationOutcome.DefinitiveSeatUnavailable:
                return await ExpireAndRequestRefundAsync(
                    request,
                    [allocation],
                    SeatConfirmationFailedReason,
                    cancellationToken,
                    new Dictionary<Guid, BookingPaymentTransitionSnapshot>
                    {
                        [snapshot.BookingId] = snapshot,
                    }).ConfigureAwait(false);

            case SeatConfirmationOutcome.TransientFailure transient:
                throw new TransientIntegrationEventException(
                    $"Trip seat confirmation is temporarily unavailable: {transient.Message}");

            default:
                throw new TransientIntegrationEventException(
                    "Trip seat confirmation returned no outcome.");
        }
    }

    private async Task<bool> HandleGroupAsync(
        ConfirmBookingOnPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var bookingIds = _bookings.QueryNoTracking()
            .Where(booking => booking.BookingGroupId == request.ReferenceId)
            .Select(booking => booking.Id)
            .OrderBy(bookingId => bookingId)
            .ToList();
        if (bookingIds.Count != 2)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Round-trip payment must reference exactly two bookings.");
        }

        await _bookings.AcquirePaymentTransitionLocksAsync(
            bookingIds,
            cancellationToken).ConfigureAwait(false);

        var allocations = _bookings.QueryNoTracking()
            .Where(booking => bookingIds.Contains(booking.Id))
            .OrderBy(booking => booking.Id)
            .Select(booking => new BookingAllocation(
                booking.Id,
                booking.PassengerUserId,
                booking.Status,
                booking.TotalAmount.Amount))
            .ToList();
        if (allocations.Count != 2)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Round-trip payment must reference exactly two bookings.");
        }

        EnsureExactPaymentAmount(request.Amount, allocations);
        if (allocations.Any(allocation => allocation.Status == BookingStatus.EXPIRED)
            || IsLateCapture(request))
        {
            return await ExpireAndRequestRefundAsync(
                request,
                allocations,
                IsLateCapture(request) ? LateCaptureReason : SeatConfirmationFailedReason,
                cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(request.Method, VnPayMethod, StringComparison.OrdinalIgnoreCase)
            && allocations.Any(allocation => allocation.Status != BookingStatus.PENDING_PAYMENT))
        {
            return await ExpireAndRequestRefundAsync(
                request,
                allocations,
                SeatConfirmationFailedReason,
                cancellationToken).ConfigureAwait(false);
        }

        if (allocations.All(allocation => allocation.Status == BookingStatus.CONFIRMED))
        {
            return false;
        }

        if (allocations.Any(allocation => allocation.Status == BookingStatus.CONFIRMED))
        {
            throw new InvalidOperationException(
                "Round-trip payment recovery found a partially confirmed booking group.");
        }

        if (allocations.Any(allocation => allocation.Status != BookingStatus.PENDING_PAYMENT))
        {
            return false;
        }

        var snapshots = new List<BookingPaymentTransitionSnapshot>(2);
        foreach (var allocation in allocations)
        {
            var snapshot = await _bookings.GetPendingPaymentTransitionSnapshotAsync(
                allocation.BookingId,
                cancellationToken).ConfigureAwait(false);
            if (snapshot is not null)
            {
                snapshots.Add(snapshot);
            }
        }

        if (snapshots.Count != 2 || snapshots.Any(snapshot => !snapshot.SeatLockToken.HasValue))
        {
            return await ExpireAndRequestRefundAsync(
                request,
                allocations,
                SeatConfirmationFailedReason,
                cancellationToken,
                snapshots.ToDictionary(snapshot => snapshot.BookingId)).ConfigureAwait(false);
        }

        static RoundTripBookSeatsLeg ToLeg(BookingPaymentTransitionSnapshot snapshot) => new(
            snapshot.TripId,
            snapshot.SeatLockToken!.Value,
            snapshot.BookingId,
            snapshot.PassengerSeatAssignments);

        var outcome = await _tripClient.ConfirmBookedRoundTripSeatsAsync(
            ToLeg(snapshots[0]),
            ToLeg(snapshots[1]),
            cancellationToken,
            request.PaymentId).ConfigureAwait(false);
        switch (outcome)
        {
            case SeatConfirmationOutcome.Success:
                {
                    var now = _clock.UtcNow;
                    var transitioned = await _bookings.TryConfirmPendingPaymentGroupAsync(
                        bookingIds,
                        now,
                        cancellationToken).ConfigureAwait(false);
                    if (!transitioned)
                    {
                        if (string.Equals(
                            request.Method,
                            VnPayMethod,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            return await CompensateConfirmationCasLossAsync(
                                request,
                                bookingIds,
                                cancellationToken).ConfigureAwait(false);
                        }

                        throw new InvalidOperationException(
                            "Round-trip payment confirmation lost its serialized Booking transition.");
                    }

                    foreach (var snapshot in snapshots.OrderBy(item => item.BookingId))
                    {
                        await RecordConfirmedBookingAsync(
                            snapshot,
                            now,
                            cancellationToken).ConfigureAwait(false);
                    }

                    return true;
                }

            case SeatConfirmationOutcome.DefinitiveSeatUnavailable:
                return await ExpireAndRequestRefundAsync(
                    request,
                    allocations,
                    SeatConfirmationFailedReason,
                    cancellationToken,
                    snapshots.ToDictionary(snapshot => snapshot.BookingId)).ConfigureAwait(false);

            case SeatConfirmationOutcome.TransientFailure transient:
                throw new TransientIntegrationEventException(
                    $"Trip round-trip seat confirmation is temporarily unavailable: {transient.Message}");

            default:
                throw new TransientIntegrationEventException(
                    "Trip round-trip seat confirmation returned no outcome.");
        }
    }

    private async Task<bool> ExpireAndRequestRefundAsync(
        ConfirmBookingOnPaymentCommand request,
        IReadOnlyList<BookingAllocation> allocations,
        string reason,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<Guid, BookingPaymentTransitionSnapshot>? knownSnapshots = null)
    {
        if (!string.Equals(request.Method, VnPayMethod, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(
                "BOOKING_SEAT_UNAVAILABLE",
                "A non-VNPay payment cannot use the captured-payment compensation flow.");
        }

        var changed = false;
        foreach (var allocation in allocations.OrderBy(item => item.BookingId))
        {
            BookingPaymentTransitionSnapshot? snapshot = null;
            var terminal = allocation.Status != BookingStatus.PENDING_PAYMENT;
            if (allocation.Status == BookingStatus.PENDING_PAYMENT)
            {
                knownSnapshots?.TryGetValue(allocation.BookingId, out snapshot);
                snapshot ??= await _bookings.GetPendingPaymentTransitionSnapshotAsync(
                    allocation.BookingId,
                    cancellationToken).ConfigureAwait(false);

                var now = _clock.UtcNow;
                terminal = await _bookings.TryExpirePendingPaymentAsync(
                    allocation.BookingId,
                    now,
                    cancellationToken).ConfigureAwait(false);
                if (terminal)
                {
                    changed = true;
                    await _statusHistory.AddAsync(
                        BookingStatusHistory.Create(
                            allocation.BookingId,
                            BookingStatus.EXPIRED,
                            now,
                            BookingStatusHistorySource.ExpireOnPayment),
                        cancellationToken).ConfigureAwait(false);
                    await CompensateSeatAndVoucherAsync(snapshot, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var observedStatus = _bookings.QueryNoTracking()
                        .Where(booking => booking.Id == allocation.BookingId)
                        .Select(booking => (BookingStatus?)booking.Status)
                        .SingleOrDefault();
                    terminal = observedStatus.HasValue
                        && observedStatus != BookingStatus.PENDING_PAYMENT;
                }
            }

            if (!terminal)
            {
                continue;
            }

            var refundEvent = new BookingPaymentRefundRequestedIntegrationEvent(
                request.PaymentId,
                request.ReferenceType.ToUpperInvariant(),
                request.ReferenceId,
                allocation.BookingId,
                allocation.PassengerUserId,
                allocation.Amount,
                reason);
            await _outbox.EnqueueAsync(
                refundEvent.EventType,
                JsonSerializer.Serialize(refundEvent, JsonOptions),
                cancellationToken).ConfigureAwait(false);
        }

        return changed;
    }

    private async Task<bool> CompensateConfirmationCasLossAsync(
        ConfirmBookingOnPaymentCommand request,
        IReadOnlyCollection<Guid> bookingIds,
        CancellationToken cancellationToken)
    {
        var observed = _bookings.QueryNoTracking()
            .Where(booking => bookingIds.Contains(booking.Id))
            .OrderBy(booking => booking.Id)
            .Select(booking => new BookingAllocation(
                booking.Id,
                booking.PassengerUserId,
                booking.Status,
                booking.TotalAmount.Amount))
            .ToList();
        if (observed.Count != bookingIds.Count)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Payment confirmation lost an authoritative Booking allocation.");
        }

        if (observed.All(allocation => allocation.Status == BookingStatus.PENDING_PAYMENT))
        {
            throw new TransientIntegrationEventException(
                "Payment confirmation lost its serialized Booking transition without a terminal winner.");
        }

        return await ExpireAndRequestRefundAsync(
            request,
            observed,
            IsLateCapture(request) ? LateCaptureReason : SeatConfirmationFailedReason,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task CompensateSeatAndVoucherAsync(
        BookingPaymentTransitionSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot is null)
        {
            return;
        }

        if (_bookingService is not null && snapshot.SeatLockToken.HasValue)
        {
            var seatNumbers = snapshot.PassengerSeatAssignments
                .Select(passenger => passenger.SeatNumber)
                .ToArray();
            if (seatNumbers.Length > 0)
            {
                await _bookingService.ReleaseSeatsAsync(
                    snapshot.TripId,
                    snapshot.SeatLockToken.Value,
                    seatNumbers,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        if (_voucherService is not null && snapshot.VoucherUsageId.HasValue)
        {
            await _voucherService.CompensateAsync(
                snapshot.BookingId,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> ConfirmPersistedBookingAsync(
        Guid paymentId,
        BookingPaymentTransitionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var transitioned = await _bookings.TryConfirmPendingPaymentAsync(
            snapshot.BookingId,
            now,
            cancellationToken).ConfigureAwait(false);
        if (!transitioned)
        {
            _logger.LogInformation(
                "Payment succeeded event {PaymentId} no-op for booking {BookingId}; another delivery already transitioned it.",
                paymentId,
                snapshot.BookingId);
            return false;
        }

        await _statusHistory.AddAsync(
            BookingStatusHistory.Create(
                snapshot.BookingId,
                BookingStatus.CONFIRMED,
                now,
                BookingStatusHistorySource.ConfirmOnPayment),
            cancellationToken).ConfigureAwait(false);

        await EnqueueBookingConfirmedAsync(snapshot, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task RecordConfirmedBookingAsync(
        BookingPaymentTransitionSnapshot snapshot,
        DateTimeOffset confirmedAt,
        CancellationToken cancellationToken)
    {
        await _statusHistory.AddAsync(
            BookingStatusHistory.Create(
                snapshot.BookingId,
                BookingStatus.CONFIRMED,
                confirmedAt,
                BookingStatusHistorySource.ConfirmOnPayment),
            cancellationToken).ConfigureAwait(false);

        await EnqueueBookingConfirmedAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnqueueBookingConfirmedAsync(
        BookingPaymentTransitionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var confirmedEvent = new
        {
            bookingId = snapshot.BookingId,
            tripId = snapshot.TripId,
            totalAmount = snapshot.TotalAmount,
            userId = snapshot.PassengerUserId,
            voucherUsageId = snapshot.VoucherUsageId,
            tickets = (snapshot.TicketIds ?? []).Select(ticketId => new
            {
                ticketId,
                passengerUserId = snapshot.PassengerUserId,
            }).ToArray(),
            ticketCodes = snapshot.TicketCodes,
            ticketCount = snapshot.TicketCodes.Count,
            shuttlePickup = snapshot.ShuttleIntent is null ? null : new
            {
                address = snapshot.ShuttleIntent.Address,
                latitude = snapshot.ShuttleIntent.Latitude,
                longitude = snapshot.ShuttleIntent.Longitude,
            },
            shuttleRequests = (snapshot.ShuttleIntents ??
                (snapshot.ShuttleIntent is null ? [] : [snapshot.ShuttleIntent]))
                .Select(intent => new
                {
                    direction = intent.Direction,
                    address = intent.Address,
                    latitude = intent.Latitude,
                    longitude = intent.Longitude,
                    roadDistanceMeters = intent.RoadDistanceMeters,
                })
                .ToArray(),
        };

        await _outbox.EnqueueAsync(
            BookingConfirmedEventType,
            JsonSerializer.Serialize(confirmedEvent, JsonOptions),
            cancellationToken).ConfigureAwait(false);
    }

    private static bool IsLateCapture(ConfirmBookingOnPaymentCommand request)
        => request.PaidAt.HasValue
            && request.DueAt.HasValue
            && request.PaidAt.Value >= request.DueAt.Value;

    private static void EnsureExactPaymentAmount(
        long paymentAmount,
        IReadOnlyCollection<BookingAllocation> allocations)
    {
        long expected;
        try
        {
            expected = allocations.Sum(allocation => checked(allocation.Amount));
        }
        catch (OverflowException)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Booking allocation amount is outside the supported range.");
        }

        if (expected != paymentAmount)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Payment amount does not match the authoritative booking allocation.");
        }
    }

    private sealed record BookingAllocation(
        Guid BookingId,
        Guid PassengerUserId,
        BookingStatus Status,
        long Amount);
}
