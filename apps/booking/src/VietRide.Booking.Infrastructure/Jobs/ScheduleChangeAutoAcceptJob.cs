using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Jobs;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.Services;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence.Outbox;

namespace VietRide.Booking.Infrastructure.Jobs;

public sealed class ScheduleChangeAutoAcceptJob(
    BookingDbContext db,
    IScheduleChangeAutoAcceptScheduler scheduler,
    IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Queue("booking")]
    [AutomaticRetry(Attempts = 5)]
    public async Task ExecuteAsync(Guid pendingActionId, CancellationToken cancellationToken)
    {
        if (pendingActionId == Guid.Empty)
        {
            return;
        }

        DateTimeOffset? terminalSchedule = null;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var action = await db.BookingPendingActions
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM vietride_booking.booking_pending_actions
                    WHERE id = {pendingActionId}
                    FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            var now = clock.UtcNow;

            if (action is null
                || action.ResolvedAt.HasValue
                || action.Reason != BookingPendingActionReason.SCHEDULE_CHANGE
                || !TryReadMetadata(action.Metadata, out var metadata))
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

            if (ScheduleChangeResolutionStateMachine.IsAutoAcceptDue(
                    action, metadata.InitialDeadline, metadata.TerminalDeadline, now))
            {
                var cutoff = ScheduleChangeResolutionStateMachine.GetEffectiveCutoff(
                    action, metadata.InitialDeadline, metadata.TerminalDeadline);
                action.AutoAcceptScheduleChange(now, cutoff);
                var eventId = DeriveAutoResolvedEventId(action.Id);
                if (!await db.OutboxEvents.AnyAsync(row => row.Id == eventId, cancellationToken))
                {
                    var integrationEvent = new BookingPendingActionAutoResolvedIntegrationEvent(
                        eventId,
                        now,
                        booking.Id,
                        booking.TripId,
                        booking.PassengerUserId,
                        action.Id,
                        "ACCEPTED",
                        metadata.Severity,
                        metadata.OldDeparture,
                        metadata.NewDeparture);
                    db.OutboxEvents.Add(new OutboxEvent
                    {
                        Id = eventId,
                        EventType = BookingPendingActionAutoResolvedIntegrationEvent.EventTypeValue,
                        Payload = JsonSerializer.Serialize(integrationEvent, JsonOptions),
                        CreatedAt = now,
                    });
                }

                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            if (ScheduleChangeResolutionStateMachine.IsMajorInitialPhaseDue(
                    action, metadata.InitialDeadline, metadata.TerminalDeadline, now))
            {
                var eventId = DeriveMajorInitialPhaseEventId(action.Id);
                if (!await db.OutboxEvents.AnyAsync(row => row.Id == eventId, cancellationToken))
                {
                    var integrationEvent = new BookingPendingActionRealertedIntegrationEvent(
                        eventId,
                        now,
                        booking.Id,
                        booking.TripId,
                        booking.PassengerUserId,
                        action.Id,
                        metadata.TerminalDeadline!.Value,
                        "SCHEDULE_CHANGE",
                        oldDeparture: metadata.OldDeparture,
                        newDeparture: metadata.NewDeparture,
                        severity: metadata.Severity);
                    db.OutboxEvents.Add(new OutboxEvent
                    {
                        Id = eventId,
                        EventType = BookingPendingActionRealertedIntegrationEvent.EventTypeValue,
                        Payload = JsonSerializer.Serialize(integrationEvent, JsonOptions),
                        CreatedAt = now,
                    });
                    await db.SaveChangesAsync(cancellationToken);
                }

                terminalSchedule = metadata.TerminalDeadline.GetValueOrDefault().AddSeconds(1);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        if (terminalSchedule.HasValue)
        {
            scheduler.EnsureScheduled(pendingActionId, terminalSchedule.Value);
        }
    }

    public static Guid DeriveMajorInitialPhaseEventId(Guid pendingActionId)
        => DeriveEventId(pendingActionId, ScheduleChangeResolutionStateMachine.MajorInitialPhase);

    public static Guid DeriveAutoResolvedEventId(Guid pendingActionId)
        => DeriveEventId(pendingActionId, "ACCEPTED");

    private static Guid DeriveEventId(Guid pendingActionId, string outcome)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"booking.booking.schedule_change:{pendingActionId:N}:{outcome}"));
        var bytes = hash[..16];
        bytes[7] = (byte)((bytes[7] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }

    private static bool TryReadMetadata(string? json, out ScheduleMetadata metadata)
    {
        metadata = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("oldDeparture", out var oldValue)
                || !oldValue.TryGetDateTimeOffset(out var oldDeparture)
                || !root.TryGetProperty("newDeparture", out var newValue)
                || !newValue.TryGetDateTimeOffset(out var newDeparture)
                || !root.TryGetProperty("severity", out var severityValue)
                || !root.TryGetProperty("initialDeadline", out var initialValue)
                || !initialValue.TryGetDateTimeOffset(out var initialDeadline)
                || !root.TryGetProperty("terminalDeadline", out var terminalValue))
            {
                return false;
            }

            var severity = severityValue.GetString();
            DateTimeOffset? terminalDeadline = terminalValue.ValueKind == JsonValueKind.Null
                ? null
                : terminalValue.TryGetDateTimeOffset(out var terminal) ? terminal : null;
            if (severity is not ("MEDIUM" or "MAJOR")
                || (severity == "MEDIUM" && terminalDeadline.HasValue)
                || (severity == "MAJOR" && !terminalDeadline.HasValue))
            {
                return false;
            }

            metadata = new ScheduleMetadata(
                oldDeparture, newDeparture, severity, initialDeadline, terminalDeadline);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private readonly record struct ScheduleMetadata(
        DateTimeOffset OldDeparture,
        DateTimeOffset NewDeparture,
        string Severity,
        DateTimeOffset InitialDeadline,
        DateTimeOffset? TerminalDeadline);
}
