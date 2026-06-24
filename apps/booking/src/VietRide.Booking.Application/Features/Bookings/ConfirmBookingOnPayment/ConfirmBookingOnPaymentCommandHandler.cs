using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Application.Features.Bookings.ConfirmBookingOnPayment;

public sealed class ConfirmBookingOnPaymentCommandHandler
    : IRequestHandler<ConfirmBookingOnPaymentCommand, bool>
{
    private const string BookingReferenceType = "BOOKING";
    private const string EventType = "booking.booking.confirmed";
    private const int SeatLockTtlSeconds = 10 * 60;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IBookingRepository _bookings;
    private readonly ITripServiceClient _tripClient;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;
    private readonly ILogger<ConfirmBookingOnPaymentCommandHandler> _logger;

    public ConfirmBookingOnPaymentCommandHandler(
        IBookingRepository bookings,
        ITripServiceClient tripClient,
        IIntegrationEventOutbox outbox,
        IClock clock,
        ILogger<ConfirmBookingOnPaymentCommandHandler> logger)
    {
        _bookings = bookings;
        _tripClient = tripClient;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task<bool> Handle(ConfirmBookingOnPaymentCommand request, CancellationToken cancellationToken)
    {
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

        var transitioned = await _bookings.TryConfirmPendingPaymentAsync(
            snapshot.BookingId,
            _clock.UtcNow,
            cancellationToken);
        if (!transitioned)
        {
            _logger.LogInformation(
                "Payment succeeded event {PaymentId} no-op for booking {BookingId}; another delivery already transitioned it.",
                request.PaymentId,
                snapshot.BookingId);
            return false;
        }

        var confirmedEvent = new
        {
            bookingId = snapshot.BookingId,
            tripId = snapshot.TripId,
            totalAmount = snapshot.TotalAmount,
            userId = snapshot.PassengerUserId,
            voucherUsageId = snapshot.VoucherUsageId,
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
