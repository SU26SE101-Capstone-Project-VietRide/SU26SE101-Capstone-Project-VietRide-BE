using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Infrastructure.Jobs;

public sealed class PendingActionRealertJob(BookingDbContext db, IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Queue("booking")]
    [AutomaticRetry(Attempts = 5)]
    public async Task ExecuteAsync(Guid pendingActionId, CancellationToken cancellationToken)
    {
        if (pendingActionId == Guid.Empty)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var action = await db.BookingPendingActions
            .FromSqlInterpolated($@"
                SELECT *
                FROM vietride_booking.booking_pending_actions
                WHERE id = {pendingActionId}
                FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        var now = clock.UtcNow;
        if (action is null || action.ResolvedAt.HasValue || now >= action.Deadline)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var deterministicEventId = DeriveEventId(pendingActionId);
        if (await db.OutboxEvents.AnyAsync(row => row.Id == deterministicEventId, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var booking = await db.Bookings
            .AsNoTracking()
            .Where(row => row.Id == action.BookingId)
            .Select(row => new { row.Id, row.TripId, row.PassengerUserId })
            .SingleOrDefaultAsync(cancellationToken);
        if (booking is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var integrationEvent = BuildEvent(action.Reason, action.Metadata, deterministicEventId, now,
            booking.Id, booking.TripId, booking.PassengerUserId, action.Id, action.Deadline);
        if (integrationEvent is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var payload = JsonSerializer.Serialize(integrationEvent, JsonOptions);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO vietride_booking.outbox_events
                (id, event_type, payload, status, retry_count, created_at)
            VALUES
                ({deterministicEventId},
                 {BookingPendingActionRealertedIntegrationEvent.EventTypeValue},
                 {payload}::jsonb,
                 'PENDING'::vietride_booking.outbox_event_status,
                 0,
                 {now})
            ON CONFLICT (id) DO NOTHING", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static BookingPendingActionRealertedIntegrationEvent? BuildEvent(
        BookingPendingActionReason reason,
        string? metadata,
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid bookingId,
        Guid tripId,
        Guid userId,
        Guid pendingActionId,
        DateTimeOffset deadline)
    {
        if (reason == BookingPendingActionReason.PENDING_SEAT_ASSIGNMENT
            && TryReadSeatMetadata(metadata, out var seatNumbers, out var seatImpactReason))
        {
            return new BookingPendingActionRealertedIntegrationEvent(
                eventId, occurredAt, bookingId, tripId, userId, pendingActionId, deadline,
                "PENDING_SEAT_ASSIGNMENT", seatNumbers, seatImpactReason);
        }

        if (reason == BookingPendingActionReason.SCHEDULE_CHANGE
            && TryReadScheduleMetadata(metadata, out var oldDeparture, out var newDeparture, out var severity))
        {
            return new BookingPendingActionRealertedIntegrationEvent(
                eventId, occurredAt, bookingId, tripId, userId, pendingActionId, deadline,
                "SCHEDULE_CHANGE", oldDeparture: oldDeparture, newDeparture: newDeparture, severity: severity);
        }

        return null;
    }

    private static bool TryReadSeatMetadata(
        string? metadata,
        out IReadOnlyCollection<string> seatNumbers,
        out string seatImpactReason)
    {
        seatNumbers = [];
        seatImpactReason = string.Empty;
        if (!TryParseMetadata(metadata, out var root)
            || !root.TryGetProperty("seatNumbers", out var seats)
            || seats.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("reason", out var reason))
        {
            return false;
        }

        seatNumbers = seats.EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => item.Length > 0)
            .ToArray();
        seatImpactReason = reason.GetString() ?? string.Empty;
        return seatNumbers.Count > 0 && seatImpactReason is
            "SEAT_REMOVED" or "SEAT_DISABLED" or "SEAT_TYPE_DOWNGRADED";
    }

    private static bool TryReadScheduleMetadata(
        string? metadata,
        out DateTimeOffset oldDeparture,
        out DateTimeOffset newDeparture,
        out string severity)
    {
        oldDeparture = default;
        newDeparture = default;
        severity = string.Empty;
        if (!TryParseMetadata(metadata, out var root)
            || !root.TryGetProperty("oldDeparture", out var oldValue)
            || !oldValue.TryGetDateTimeOffset(out oldDeparture)
            || !root.TryGetProperty("newDeparture", out var newValue)
            || !newValue.TryGetDateTimeOffset(out newDeparture)
            || !root.TryGetProperty("severity", out var severityValue))
        {
            return false;
        }

        severity = severityValue.GetString() ?? string.Empty;
        return severity is "MEDIUM" or "MAJOR";
    }

    private static bool TryParseMetadata(string? metadata, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(metadata);
            root = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static Guid DeriveEventId(Guid pendingActionId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"booking.booking.pending_action_realerted:{pendingActionId:N}"));
        var bytes = hash[..16];
        bytes[7] = (byte)((bytes[7] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }
}
