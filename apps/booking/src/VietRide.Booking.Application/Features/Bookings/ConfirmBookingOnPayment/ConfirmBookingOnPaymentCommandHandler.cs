using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Domain.Constants;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Application.Features.Bookings.ConfirmBookingOnPayment;

public sealed class ConfirmBookingOnPaymentCommandHandler
    : IRequestHandler<ConfirmBookingOnPaymentCommand, bool>
{
    private const string BookingReferenceType = "BOOKING";
    private const string BookingGroupReferenceType = "BOOKING_GROUP";
    private const string EventType = "booking.booking.confirmed";
    private const int SeatLockTtlSeconds = 10 * 60;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IBookingRepository _bookings;
    private readonly IBookingStatusHistoryRepository _statusHistory;
    private readonly ITripServiceClient _tripClient;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;
    private readonly ILogger<ConfirmBookingOnPaymentCommandHandler> _logger;

    public ConfirmBookingOnPaymentCommandHandler(
        IBookingRepository bookings,
        ITripServiceClient tripClient,
        IIntegrationEventOutbox outbox,
        IClock clock,
        ILogger<ConfirmBookingOnPaymentCommandHandler> logger,
        IBookingStatusHistoryRepository statusHistory)
    {
        _bookings = bookings;
        _statusHistory = statusHistory;
        _tripClient = tripClient;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task<bool> Handle(ConfirmBookingOnPaymentCommand request, CancellationToken cancellationToken)
    {
        if (string.Equals(request.ReferenceType, BookingGroupReferenceType, StringComparison.OrdinalIgnoreCase))
        {
            var group = _bookings.QueryNoTracking()
                .Where(x => x.BookingGroupId == request.ReferenceId)
                .Select(x => new { x.Id, Amount = x.TotalAmount.Amount })
                .ToList();
            if (group.Count != 2 || group.Sum(x => x.Amount) != request.Amount)
            {
                throw new CodedValidationException("PAYMENT_AMOUNT_MISMATCH", "Round-trip payment amount does not match exactly two bookings.");
            }

            var snapshots = new List<BookingPaymentTransitionSnapshot>();
            foreach (var booking in group.OrderBy(x => x.Id))
            {
                var groupSnapshot = await _bookings.GetPendingPaymentTransitionSnapshotAsync(booking.Id, cancellationToken);
                if (groupSnapshot is not null) snapshots.Add(groupSnapshot);
            }
            if (snapshots.Count == 0) return false;
            if (snapshots.Count != 2 || snapshots.Any(x => !x.SeatLockToken.HasValue))
                throw new ConflictException("BOOKING_SEAT_UNAVAILABLE", "Round-trip seat locks are no longer available.");

            static RoundTripBookSeatsLeg ToLeg(BookingPaymentTransitionSnapshot snapshot) => new(
                snapshot.TripId, snapshot.SeatLockToken!.Value, snapshot.BookingId, snapshot.PassengerSeatAssignments);
            if (!await _tripClient.BookRoundTripSeatsAsync(ToLeg(snapshots[0]), ToLeg(snapshots[1]), cancellationToken))
                throw new ConflictException("BOOKING_SEAT_UNAVAILABLE", "Round-trip seat locks expired before confirmation.");

            var changed = false;
            foreach (var groupSnapshot in snapshots.OrderBy(x => x.BookingId))
            {
                changed |= await ConfirmPersistedBookingAsync(
                    request.PaymentId,
                    groupSnapshot,
                    cancellationToken);
            }

            return changed;
        }

        if (!string.Equals(request.ReferenceType, BookingReferenceType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var snapshot = await _bookings.GetPendingPaymentTransitionSnapshotAsync(
            request.ReferenceId,
            cancellationToken);
        if (snapshot is null)
        {
            _logger.LogInformation(
                "Payment succeeded event {PaymentId} ignored for booking {BookingId}; booking is not pending payment.",
                request.PaymentId,
                request.ReferenceId);
            return false;
        }

        // Seat booking is idempotent on the Trip side, so book first (self-healing: a crash before the
        // Confirm commit leaves the booking PENDING_PAYMENT and re-delivery safely re-books + confirms).
        // But a CONCURRENT re-delivery can lose the BookSeats/lock race after the winner already
        // confirmed+booked — in that case do NOT throw (which would DLQ a correctly-confirmed booking):
        // re-check status and treat an already-transitioned booking as an idempotent no-op.
        try
        {
            var seatLock = await ReplaySeatLockAsync(snapshot, cancellationToken);
            var booked = await _tripClient.BookSeatsAsync(
                snapshot.TripId,
                seatLock.SeatLockToken,
                snapshot.BookingId,
                snapshot.PassengerSeatAssignments,
                cancellationToken);
            if (!booked)
            {
                throw new ConflictException(
                    "BOOKING_SEAT_UNAVAILABLE",
                    "Seat lock expired before booking could be confirmed.");
            }
        }
        catch (ConflictException)
        {
            var recheck = await _bookings.GetPendingPaymentTransitionSnapshotAsync(
                snapshot.BookingId,
                cancellationToken);
            if (recheck is null)
            {
                _logger.LogInformation(
                    "Payment succeeded event {PaymentId} no-op for booking {BookingId}; another delivery already confirmed it before seat re-book.",
                    request.PaymentId,
                    snapshot.BookingId);
                return false;
            }

            throw;
        }

        return await ConfirmPersistedBookingAsync(request.PaymentId, snapshot, cancellationToken);
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
            cancellationToken);
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
            cancellationToken);

        var confirmedEvent = new
        {
            bookingId = snapshot.BookingId,
            tripId = snapshot.TripId,
            totalAmount = snapshot.TotalAmount,
            userId = snapshot.PassengerUserId,
            voucherUsageId = snapshot.VoucherUsageId,
            ticketCodes = snapshot.TicketCodes,
            ticketCount = snapshot.TicketCodes.Count,
        };

        await _outbox.EnqueueAsync(
            EventType,
            JsonSerializer.Serialize(confirmedEvent, JsonOptions),
            cancellationToken);

        return true;
    }

    private async Task<SeatLockResult> ReplaySeatLockAsync(
        BookingPaymentTransitionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var seatNumbers = snapshot.PassengerSeatAssignments
            .Select(assignment => assignment.SeatNumber)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var idempotencyKey = BuildCreateBookingLockIdempotencyKey(
            snapshot.PassengerUserId,
            snapshot.TripId,
            seatNumbers);

        var outcome = await _tripClient.LockSeatsAsync(
            snapshot.TripId,
            seatNumbers,
            snapshot.PassengerUserId,
            idempotencyKey,
            SeatLockTtlSeconds,
            cancellationToken);

        return outcome switch
        {
            LockSeatsOutcome.Success success => success.Data,
            LockSeatsOutcome.SeatUnavailable unavailable => throw new ConflictException(
                "BOOKING_SEAT_UNAVAILABLE",
                $"One or more seats are unavailable: {string.Join(", ", unavailable.UnavailableSeats)}."),
            LockSeatsOutcome.TripNotBookable notBookable => throw new ConflictException(
                "BOOKING_TRIP_NOT_BOOKABLE",
                notBookable.Message),
            LockSeatsOutcome.TripNotFound => throw new CodedNotFoundException(
                "TRIP_NOT_FOUND",
                $"Trip '{snapshot.TripId}' not found."),
            LockSeatsOutcome.TransportError transportError => throw new InvalidOperationException(
                $"Seat lock replay failed: {transportError.Message}"),
            _ => throw new InvalidOperationException("Seat lock replay failed."),
        };
    }

    private static string BuildCreateBookingLockIdempotencyKey(
        Guid passengerUserId,
        Guid tripId,
        IReadOnlyList<string> seatNumbers)
        => $"lock-{passengerUserId}-{tripId}-{string.Join(",", seatNumbers)}";
}
