using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Application.Exceptions;
using VietRide.Booking.Domain.Constants;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Serialization;
using VietRide.Shared.Kernel.Time;
using VietRide.Shared.Persistence.Outbox;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.Infrastructure.Jobs;

public sealed class NoShowDetectionJob(
    BookingDbContext db,
    IBookingRepository bookings,
    IBookingStatusHistoryRepository histories,
    ITripServiceClient trips,
    IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = UtcJson.IgnoreNullOptions;

    [Queue("booking")]
    [AutomaticRetry(Attempts = 5)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var candidates = await bookings.GetNoShowCandidatesAsync(cancellationToken);
        foreach (var candidate in candidates)
        {
            var trip = await trips.GetOperationalTripSnapshotAsync(candidate.TripId, cancellationToken);
            if (trip is null)
            {
                throw new BookingUpstreamUnavailableException("Trip operational snapshot is unavailable.");
            }

            if (HasMissingRequiredAnchor(candidate, trip))
            {
                throw new BookingUpstreamUnavailableException(
                    "Trip operational snapshot is missing the required no-show anchor.");
            }

            if (!TryResolveTrigger(candidate, trip, now, out var triggerType))
            {
                continue;
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var booking = await bookings.FindConfirmedWithPassengersForUpdateAsync(candidate.Id, cancellationToken);
            if (booking is null || !TryResolveTrigger(booking, trip, now, out triggerType))
            {
                await transaction.CommitAsync(cancellationToken);
                continue;
            }

            var newlyMarked = booking.MarkPendingPassengersNoShow();
            if (newlyMarked.Count == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                continue;
            }

            await histories.AddAsync(BookingStatusHistory.Create(
                booking.Id,
                booking.Status,
                now,
                BookingStatusHistorySource.MarkNoShow), cancellationToken);
            var statDate = BusinessTime.ToLocalDate(now);
            var statsId = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO vietride_booking.booking_stats (
    id, operator_id, stat_date, trip_id, total_no_show_passengers, updated_at
)
VALUES (
    {statsId}, {booking.OperatorId}, {statDate}, {booking.TripId}, {newlyMarked.Count}, now()
)
ON CONFLICT (operator_id, stat_date, COALESCE(trip_id, '00000000-0000-0000-0000-000000000000'::uuid))
DO UPDATE SET
    total_no_show_passengers = booking_stats.total_no_show_passengers + EXCLUDED.total_no_show_passengers,
    updated_at = now();", cancellationToken);
            var eventId = DeriveEventId(booking.Id, booking.Status);
            var evt = new BookingPassengerNoShowMarkedIntegrationEvent(
                eventId,
                now,
                booking.Id,
                booking.TripId,
                booking.PassengerUserId,
                booking.Status.ToString(),
                newlyMarked,
                triggerType,
                booking.PickupStopId);
            db.OutboxEvents.Add(new OutboxEvent
            {
                Id = eventId,
                EventType = evt.EventType,
                Payload = JsonSerializer.Serialize(evt, JsonOptions),
                CreatedAt = now,
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public static bool TryResolveTrigger(
        BookingEntity booking,
        TripSnapshot trip,
        DateTimeOffset now,
        out string triggerType)
    {
        triggerType = string.Empty;
        if (booking.Status != BookingStatus.CONFIRMED)
        {
            return false;
        }

        if (booking.PickupStopId.HasValue)
        {
            var stop = trip.Stops.SingleOrDefault(item => item.StopId == booking.PickupStopId.Value);
            if (stop?.Status == "ARRIVED"
                && stop.ActualArrivalTime.HasValue
                && stop.ActualArrivalTime.Value.AddMinutes(15) < now)
            {
                triggerType = "ALONG_ROUTE";
                return true;
            }

            return false;
        }

        if (booking.PickupStationId.HasValue
            && trip.Status == "IN_PROGRESS"
            && trip.ActualDepartureTime.HasValue
            && trip.ActualDepartureTime.Value.AddMinutes(15) < now)
        {
            triggerType = "TERMINAL";
            return true;
        }

        return false;
    }

    public static Guid DeriveEventId(Guid bookingId, BookingStatus status)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"booking.booking.passenger_no_show_marked:{bookingId:N}:{status}"))[..16];
        bytes[7] = (byte)((bytes[7] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }

    private static bool HasMissingRequiredAnchor(BookingEntity booking, TripSnapshot trip)
    {
        if (booking.PickupStopId.HasValue)
        {
            var stop = trip.Stops.SingleOrDefault(item => item.StopId == booking.PickupStopId.Value);
            return stop is null || (stop.Status == "ARRIVED" && !stop.ActualArrivalTime.HasValue);
        }

        return booking.PickupStationId.HasValue
            && trip.Status == "IN_PROGRESS"
            && !trip.ActualDepartureTime.HasValue;
    }
}
