using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Messaging;

internal sealed class BookingShuttleCancelledIntegrationEventHandler
    : IIntegrationEventHandler<BookingShuttleCancelledIntegrationEvent>
{
    private readonly TripDbContext _db;
    private readonly ILogger<BookingShuttleCancelledIntegrationEvent> _logger;

    public BookingShuttleCancelledIntegrationEventHandler(
        TripDbContext db,
        ILogger<BookingShuttleCancelledIntegrationEvent> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task HandleAsync(
        BookingShuttleCancelledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        integrationEvent.Validate();

        var manifests = await _db.ShuttlePassengers
            .Where(passenger => passenger.BookingId == integrationEvent.BookingId!.Value
                && passenger.Status != Domain.Entities.ShuttlePassenger.CancelledStatus
                && passenger.Status != Domain.Entities.ShuttlePassenger.DeliveredStatus)
            .ToArrayAsync(cancellationToken);

        foreach (var manifest in manifests)
        {
            manifest.Cancel("BOOKING_CANCELLED");
        }

        if (integrationEvent.HasOperationalSeatData
            && integrationEvent.PreviousStatus == "CONFIRMED")
        {
            var bookingId = integrationEvent.BookingId!.Value;
            var tripId = integrationEvent.TripId!.Value;
            var ownedSeats = await _db.TripSeats
                .Where(seat => seat.TripId == tripId
                    && seat.BookingId == bookingId
                    && seat.Status == TripSeatStatus.BOOKED)
                .ToArrayAsync(cancellationToken);

            var payloadSeatNumbers = integrationEvent.SeatNumbers!
                .Select(seatNumber => seatNumber.Trim().ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var ownedSeatNumbers = ownedSeats
                .Select(seat => seat.SeatNumber)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (!payloadSeatNumbers.SequenceEqual(ownedSeatNumbers, StringComparer.Ordinal))
            {
                _logger.LogWarning(
                    "Booking cancellation seat ownership mismatch for booking {BookingId}, trip {TripId}. Payload seats: {PayloadSeatNumbers}; owned booked seats: {OwnedSeatNumbers}.",
                    bookingId,
                    tripId,
                    string.Join(',', payloadSeatNumbers),
                    string.Join(',', ownedSeatNumbers));
            }

            foreach (var seat in ownedSeats)
            {
                seat.ReleaseBooked(bookingId);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
