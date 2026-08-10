using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Serialization;
using VietRide.Shared.Persistence.Outbox;

namespace VietRide.Booking.Infrastructure.Jobs;

public sealed class StopDisabledAutoFallbackJob(BookingDbContext db, IBookingPendingActionRepository actions, IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = UtcJson.Options;

    [Queue("booking")]
    [AutomaticRetry(Attempts = 5)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var candidates = await actions.GetExpiredStopDisabledCandidatesAsync(now, cancellationToken);
        foreach (var candidate in candidates)
        {
            var booking = await db.Bookings
                .FromSqlInterpolated($"""
                    SELECT * FROM vietride_booking.bookings
                    WHERE id = {candidate.BookingId}
                    FOR UPDATE
                    """)
                .SingleOrDefaultAsync(cancellationToken);
            if (booking is null) continue;

            var action = await actions.GetByIdForUpdateSkipLockedAsync(candidate.Id, cancellationToken);
            if (action is null
                || action.BookingId != booking.Id
                || action.Reason != BookingPendingActionReason.STOP_DISABLED
                || action.ResolvedAt.HasValue
                || action.Deadline >= now
                || !TryReadMetadata(action.Metadata, out var metadata))
            {
                continue;
            }

            if (metadata.AffectedField == "PICKUP")
            {
                booking.ChangePickup(metadata.FallbackStationId, null);
            }
            else
            {
                booking.ChangeDropoff(metadata.FallbackStationId, null);
            }

            action.Resolve(BookingPendingActionResolved.AUTO_FALLBACK_DESTINATION, now);
            var eventId = DeriveEventId(action.Id);
            if (!await db.OutboxEvents.AnyAsync(x => x.Id == eventId, cancellationToken))
            {
                var evt = new BookingStopDisabledAutoFallbackIntegrationEvent(eventId, now, booking.Id, booking.TripId,
                    booking.PassengerUserId, action.Id, metadata.DisabledStopId, metadata.AffectedField, metadata.FallbackStationId);
                db.OutboxEvents.Add(new OutboxEvent
                {
                    Id = eventId,
                    EventType = evt.EventType,
                    Payload = JsonSerializer.Serialize(evt, JsonOptions),
                    CreatedAt = now
                });
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static Guid DeriveEventId(Guid pendingActionId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"booking.booking.stop_disabled_auto_fallback_applied:{pendingActionId:N}"))[..16];
        bytes[7] = (byte)((bytes[7] & 0x0f) | 0x50); bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }

    private static bool TryReadMetadata(string? json, out Metadata metadata)
    {
        metadata = default;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
            if (!root.TryGetProperty("disabledStopId", out var d) || !d.TryGetGuid(out var disabled)
                || !root.TryGetProperty("affectedField", out var f) || f.GetString() is not ("PICKUP" or "DROPOFF")
                || !root.TryGetProperty("fallbackStationId", out var s) || !s.TryGetGuid(out var station)) return false;
            metadata = new Metadata(disabled, f.GetString()!, station); return true;
        }
        catch (JsonException) { return false; }
    }

    private readonly record struct Metadata(Guid DisabledStopId, string AffectedField, Guid FallbackStationId);
}
