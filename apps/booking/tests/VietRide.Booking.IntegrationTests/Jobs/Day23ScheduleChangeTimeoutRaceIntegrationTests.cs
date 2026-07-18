using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Jobs;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Infrastructure.Jobs;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.IntegrationTests.Jobs;

public sealed class Day23ScheduleChangeTimeoutRaceIntegrationTests(
    Day23ScheduleChangeTimeoutFixture fixture)
    : IClassFixture<Day23ScheduleChangeTimeoutFixture>
{
    [Fact]
    public async Task DuplicateTerminalJobs_ResolveOnceWithCanonicalIdentityAndNoCancellationOrRefund()
    {
        var initial = DateTimeOffset.Parse("2026-07-17T10:00:00Z");
        var terminal = initial.AddHours(1);
        var now = terminal.AddTicks(1);
        var seeded = await fixture.SeedAsync(BookingPendingActionSeverity.MAJOR, initial, terminal);

        await Task.WhenAll(ExecuteAsync(seeded.ActionId, now), ExecuteAsync(seeded.ActionId, now));

        await using var verify = fixture.CreateDb(now);
        var action = await verify.BookingPendingActions.AsNoTracking()
            .SingleAsync(row => row.Id == seeded.ActionId);
        var booking = await verify.Bookings.AsNoTracking().SingleAsync(row => row.Id == seeded.BookingId);
        var eventId = ScheduleChangeAutoAcceptJob.DeriveAutoResolvedEventId(seeded.ActionId);
        var outbox = await verify.OutboxEvents.AsNoTracking().SingleAsync(row => row.Id == eventId);
        action.ResolvedAction.Should().Be(BookingPendingActionResolved.ACCEPTED);
        action.ResolvedAt.Should().BeCloseTo(now, TimeSpan.FromMicroseconds(1));
        booking.Status.Should().Be(BookingStatus.CONFIRMED);
        booking.RefundOverride.Should().BeFalse();
        booking.CancellationReason.Should().BeNull();
        outbox.EventType.Should().Be(BookingPendingActionAutoResolvedIntegrationEvent.EventTypeValue);
        using var payload = JsonDocument.Parse(outbox.Payload);
        payload.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            ["eventId", "occurredAt", "bookingId", "tripId", "userId", "pendingActionId", "resolvedAction", "severity", "oldDeparture", "newDeparture"]);
        payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(outbox.Id);
        payload.RootElement.GetProperty("resolvedAction").GetString().Should().Be("ACCEPTED");
        payload.RootElement.GetProperty("severity").GetString().Should().Be("MAJOR");
        (await verify.OutboxEvents.CountAsync(row =>
            row.EventType == BookingPendingActionRealertedIntegrationEvent.EventTypeValue)).Should().Be(0);
    }

    [Fact]
    public async Task PassengerAtEqualityWinsAndLaterJobIsDurableNoOp()
    {
        var cutoff = DateTimeOffset.Parse("2026-07-17T10:00:00Z");
        var seeded = await fixture.SeedAsync(BookingPendingActionSeverity.MEDIUM, cutoff, null);
        await using (var passengerDb = fixture.CreateDb(cutoff))
        {
            var action = await passengerDb.BookingPendingActions.SingleAsync(row => row.Id == seeded.ActionId);
            action.ResolveScheduleChange(BookingPendingActionResolved.ACCEPTED, cutoff, cutoff);
            await passengerDb.SaveChangesAsync();
        }

        await ExecuteAsync(seeded.ActionId, cutoff.AddTicks(1));

        await using var verify = fixture.CreateDb(cutoff.AddTicks(1));
        (await verify.OutboxEvents.CountAsync(row =>
            row.EventType == BookingPendingActionAutoResolvedIntegrationEvent.EventTypeValue)).Should().Be(0);
        var actionAfter = await verify.BookingPendingActions.AsNoTracking()
            .SingleAsync(row => row.Id == seeded.ActionId);
        actionAfter.ResolvedAt.Should().Be(cutoff);
        actionAfter.ResolvedAction.Should().Be(BookingPendingActionResolved.ACCEPTED);
    }

    [Fact]
    public async Task JobWinner_PreventsPassengerTerminalTransitionAndKeepsOneOutcome()
    {
        var cutoff = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        var now = cutoff.AddTicks(1);
        var seeded = await fixture.SeedAsync(BookingPendingActionSeverity.MEDIUM, cutoff, null);

        await ExecuteAsync(seeded.ActionId, now);

        await using var passengerDb = fixture.CreateDb(now);
        var action = await passengerDb.BookingPendingActions.SingleAsync(row => row.Id == seeded.ActionId);
        var passengerAttempt = () => action.ResolveScheduleChange(
            BookingPendingActionResolved.REJECTED,
            cutoff,
            cutoff);
        passengerAttempt.Should().Throw<InvalidOperationException>();
        (await passengerDb.OutboxEvents.CountAsync(row =>
            row.Id == ScheduleChangeAutoAcceptJob.DeriveAutoResolvedEventId(seeded.ActionId)))
            .Should().Be(1);
        (await passengerDb.Bookings.AsNoTracking().SingleAsync(row => row.Id == seeded.BookingId))
            .Status.Should().Be(BookingStatus.CONFIRMED);
    }

    [Fact]
    public async Task PersistenceFailure_RollsBackResolutionAndOutboxAtomically()
    {
        var cutoff = DateTimeOffset.Parse("2026-07-19T10:00:00Z");
        var now = cutoff.AddTicks(1);
        var seeded = await fixture.SeedAsync(BookingPendingActionSeverity.MEDIUM, cutoff, null);
        await using (var setup = fixture.CreateDb(now))
        {
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE OR REPLACE FUNCTION vietride_booking.reject_timeout_resolution()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.resolved_at IS NOT NULL AND OLD.resolved_at IS NULL THEN
                        RAISE EXCEPTION 'forced timeout rollback';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER reject_timeout_resolution_trigger
                BEFORE UPDATE ON vietride_booking.booking_pending_actions
                FOR EACH ROW EXECUTE FUNCTION vietride_booking.reject_timeout_resolution();
                """);
        }

        try
        {
            var act = () => ExecuteAsync(seeded.ActionId, now);
            await act.Should().ThrowAsync<DbUpdateException>();

            await using var verify = fixture.CreateDb(now);
            var action = await verify.BookingPendingActions.AsNoTracking()
                .SingleAsync(row => row.Id == seeded.ActionId);
            action.ResolvedAt.Should().BeNull();
            action.ResolvedAction.Should().BeNull();
            (await verify.OutboxEvents.CountAsync(row =>
                row.Id == ScheduleChangeAutoAcceptJob.DeriveAutoResolvedEventId(seeded.ActionId)))
                .Should().Be(0);
        }
        finally
        {
            await using var cleanup = fixture.CreateDb(now);
            await cleanup.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER IF EXISTS reject_timeout_resolution_trigger
                    ON vietride_booking.booking_pending_actions;
                DROP FUNCTION IF EXISTS vietride_booking.reject_timeout_resolution();
                """);
        }
    }

    private async Task ExecuteAsync(Guid actionId, DateTimeOffset now)
    {
        await using var db = fixture.CreateDb(now);
        await new ScheduleChangeAutoAcceptJob(
                db,
                Substitute.For<IScheduleChangeAutoAcceptScheduler>(),
                Clock(now))
            .ExecuteAsync(actionId, CancellationToken.None);
    }

    private static IClock Clock(DateTimeOffset now)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        return clock;
    }
}
