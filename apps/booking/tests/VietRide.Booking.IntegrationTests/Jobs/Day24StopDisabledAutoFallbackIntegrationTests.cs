using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Infrastructure.Jobs;
using VietRide.Booking.IntegrationTests.Messaging;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence.Outbox;

namespace VietRide.Booking.IntegrationTests.Jobs;

public sealed class Day24StopDisabledAutoFallbackIntegrationTests(
    Day24StopDisabledAutoFallbackFixture fixture)
    : IClassFixture<Day24StopDisabledAutoFallbackFixture>
{
    [Fact]
    public async Task ExpiredPickupAndDropoff_AreMappedAndEmitOneExactPendingOutboxEach()
    {
        var deadline = DateTimeOffset.Parse("2026-07-19T10:00:00Z");
        var now = deadline.AddMilliseconds(1);
        var pickup = await fixture.SeedAsync(deadline, "PICKUP");
        var dropoff = await fixture.SeedAsync(deadline, "DROPOFF");

        await ExecuteAsync(now);

        await using var verify = fixture.CreateDb(now);
        foreach (var seeded in new[] { pickup, dropoff })
        {
            var booking = await verify.Bookings.AsNoTracking().SingleAsync(row => row.Id == seeded.BookingId);
            var action = await verify.BookingPendingActions.AsNoTracking().SingleAsync(row => row.Id == seeded.ActionId);
            var outbox = await verify.OutboxEvents.AsNoTracking()
                .SingleAsync(row => row.Id == StopDisabledAutoFallbackJob.DeriveEventId(seeded.ActionId));

            booking.Status.Should().Be(BookingStatus.CONFIRMED);
            if (seeded.AffectedField == "PICKUP")
            {
                booking.PickupStationId.Should().Be(seeded.FallbackStationId);
                booking.PickupStopId.Should().BeNull();
            }
            else
            {
                booking.DropoffStationId.Should().Be(seeded.FallbackStationId);
                booking.DropoffStopId.Should().BeNull();
            }

            action.ResolvedAction.Should().Be(BookingPendingActionResolved.AUTO_FALLBACK_DESTINATION);
            action.ResolvedAt.Should().Be(now);
            outbox.EventType.Should().Be(BookingStopDisabledAutoFallbackIntegrationEvent.EventTypeValue);
            outbox.Status.Should().Be(OutboxEventStatus.PENDING);
            outbox.PublishedAt.Should().BeNull();
            using var payload = JsonDocument.Parse(outbox.Payload);
            payload.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
                ["eventId", "occurredAt", "eventType", "bookingId", "tripId", "userId", "pendingActionId", "disabledStopId", "affectedField", "fallbackStationId", "resolvedAction"]);
            payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(outbox.Id);
            payload.RootElement.GetProperty("eventType").GetString().Should().Be(outbox.EventType);
            payload.RootElement.GetProperty("bookingId").GetGuid().Should().Be(seeded.BookingId);
            payload.RootElement.GetProperty("tripId").GetGuid().Should().Be(seeded.TripId);
            payload.RootElement.GetProperty("userId").GetGuid().Should().Be(seeded.UserId);
            payload.RootElement.GetProperty("pendingActionId").GetGuid().Should().Be(seeded.ActionId);
            payload.RootElement.GetProperty("disabledStopId").GetGuid().Should().Be(seeded.DisabledStopId);
            payload.RootElement.GetProperty("affectedField").GetString().Should().Be(seeded.AffectedField);
            payload.RootElement.GetProperty("fallbackStationId").GetGuid().Should().Be(seeded.FallbackStationId);
            payload.RootElement.GetProperty("resolvedAction").GetString().Should().Be("AUTO_FALLBACK_DESTINATION");
        }

        (await verify.OutboxEvents.CountAsync()).Should().Be(2);
        (await verify.OutboxEvents.CountAsync(row =>
            row.EventType == "booking.booking.pending_action_auto_resolved"
            || row.EventType.Contains("cancelled")
            || row.EventType.Contains("refund"))).Should().Be(0);
    }

    [Fact]
    public async Task EqualityIsUntouched_UntilFrozenClockAdvancesPastDeadline()
    {
        var deadline = DateTimeOffset.Parse("2026-07-20T10:00:00Z");
        var seeded = await fixture.SeedAsync(deadline, "PICKUP");

        await ExecuteAsync(deadline);

        await using (var atEquality = fixture.CreateDb(deadline))
        {
            var action = await atEquality.BookingPendingActions.AsNoTracking()
                .SingleAsync(row => row.Id == seeded.ActionId);
            action.ResolvedAt.Should().BeNull();
            (await atEquality.OutboxEvents.CountAsync(row =>
                row.Id == StopDisabledAutoFallbackJob.DeriveEventId(seeded.ActionId))).Should().Be(0);
        }

        var nextPass = deadline.AddMinutes(5);
        await ExecuteAsync(nextPass);

        await using var after = fixture.CreateDb(nextPass);
        (await after.BookingPendingActions.AsNoTracking().SingleAsync(row => row.Id == seeded.ActionId))
            .ResolvedAt.Should().Be(nextPass);
        (await after.OutboxEvents.CountAsync(row =>
            row.Id == StopDisabledAutoFallbackJob.DeriveEventId(seeded.ActionId))).Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentExecutionsAndRerun_ResolveAndEmitExactlyOnce()
    {
        var deadline = DateTimeOffset.Parse("2026-07-21T10:00:00Z");
        var now = deadline.AddMinutes(5);
        var seeded = await fixture.SeedAsync(deadline, "DROPOFF");

        await Task.WhenAll(ExecuteAsync(now), ExecuteAsync(now));
        await ExecuteAsync(now.AddMinutes(5));

        await using var verify = fixture.CreateDb(now);
        (await verify.OutboxEvents.CountAsync(row =>
            row.Id == StopDisabledAutoFallbackJob.DeriveEventId(seeded.ActionId))).Should().Be(1);
        var action = await verify.BookingPendingActions.AsNoTracking().SingleAsync(row => row.Id == seeded.ActionId);
        action.ResolvedAt.Should().Be(now);
        action.ResolvedAction.Should().Be(BookingPendingActionResolved.AUTO_FALLBACK_DESTINATION);
    }

    [Fact]
    public async Task SchedulerAndPassengerReplacement_UseBookingThenActionOrderWithoutDeadlock()
    {
        var deadline = DateTimeOffset.Parse("2026-07-22T10:00:00Z");
        var now = deadline.AddMinutes(5);
        var seeded = await fixture.SeedAsync(deadline, "PICKUP");
        var passengerReplacementStationId = Guid.NewGuid();
        var schedulerAtActionLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseScheduler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var passengerAtBookingLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var scheduler = ExecuteAsync(now, schedulerAtActionLock, releaseScheduler);
        await schedulerAtActionLock.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var passenger = PassengerReplacementAsync(
            seeded,
            passengerReplacementStationId,
            now,
            passengerAtBookingLock);
        await passengerAtBookingLock.Task.WaitAsync(TimeSpan.FromSeconds(10));
        releaseScheduler.TrySetResult();

        await Task.WhenAll(scheduler, passenger).WaitAsync(TimeSpan.FromSeconds(10));
        await ExecuteAsync(now.AddMinutes(5));

        await using var verify = fixture.CreateDb(now);
        var booking = await verify.Bookings.AsNoTracking().SingleAsync(row => row.Id == seeded.BookingId);
        var action = await verify.BookingPendingActions.AsNoTracking().SingleAsync(row => row.Id == seeded.ActionId);
        booking.PickupStationId.Should().Be(seeded.FallbackStationId);
        booking.PickupStationId.Should().NotBe(passengerReplacementStationId);
        booking.PickupStopId.Should().BeNull();
        action.ResolvedAction.Should().Be(BookingPendingActionResolved.AUTO_FALLBACK_DESTINATION);
        (await verify.OutboxEvents.CountAsync(row =>
            row.Id == StopDisabledAutoFallbackJob.DeriveEventId(seeded.ActionId))).Should().Be(1);
    }

    [Fact]
    public async Task PassengerHoldingActionThenRequestingBooking_IsSkippedWithoutDeadlockAndNextPassWinsOnce()
    {
        var deadline = DateTimeOffset.Parse("2026-07-23T10:00:00Z");
        var now = deadline.AddMinutes(5);
        var seeded = await fixture.SeedAsync(deadline, "DROPOFF");
        var passengerHoldsAction = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var passengerMayRequestBooking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var passenger = PassengerActionFirstExpiredAttemptAsync(
            seeded,
            now,
            passengerHoldsAction,
            passengerMayRequestBooking);
        await passengerHoldsAction.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await ExecuteAsync(now).WaitAsync(TimeSpan.FromSeconds(10));
        passengerMayRequestBooking.TrySetResult();
        await passenger.WaitAsync(TimeSpan.FromSeconds(10));

        await using (var skippedPass = fixture.CreateDb(now))
        {
            var unresolved = await skippedPass.BookingPendingActions.AsNoTracking()
                .SingleAsync(row => row.Id == seeded.ActionId);
            unresolved.ResolvedAt.Should().BeNull();
            (await skippedPass.OutboxEvents.CountAsync(row =>
                row.Id == StopDisabledAutoFallbackJob.DeriveEventId(seeded.ActionId))).Should().Be(0);
        }

        var nextPass = now.AddMinutes(5);
        await ExecuteAsync(nextPass);
        await ExecuteAsync(nextPass.AddMinutes(5));

        await using var verify = fixture.CreateDb(nextPass);
        var booking = await verify.Bookings.AsNoTracking().SingleAsync(row => row.Id == seeded.BookingId);
        var action = await verify.BookingPendingActions.AsNoTracking().SingleAsync(row => row.Id == seeded.ActionId);
        booking.DropoffStationId.Should().Be(seeded.FallbackStationId);
        booking.DropoffStopId.Should().BeNull();
        action.ResolvedAction.Should().Be(BookingPendingActionResolved.AUTO_FALLBACK_DESTINATION);
        action.ResolvedAt.Should().Be(nextPass);
        (await verify.OutboxEvents.CountAsync(row =>
            row.Id == StopDisabledAutoFallbackJob.DeriveEventId(seeded.ActionId))).Should().Be(1);
    }

    private async Task ExecuteAsync(DateTimeOffset now)
    {
        await using var db = fixture.CreateDb(now);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        await new StopDisabledAutoFallbackJob(
                db,
                Day22EventDatabase.CreatePendingActionRepository(db),
                clock)
            .ExecuteAsync(CancellationToken.None);
    }

    private async Task ExecuteAsync(
        DateTimeOffset now,
        TaskCompletionSource actionLockRequested,
        TaskCompletionSource releaseActionLock)
    {
        await using var db = fixture.CreateDb(now);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var repository = new CoordinatedPendingActionRepository(
            Day22EventDatabase.CreatePendingActionRepository(db),
            actionLockRequested,
            releaseActionLock);
        await new StopDisabledAutoFallbackJob(db, repository, clock)
            .ExecuteAsync(CancellationToken.None);
    }

    private async Task PassengerReplacementAsync(
        SeededFallback seeded,
        Guid replacementStationId,
        DateTimeOffset now,
        TaskCompletionSource bookingLockRequested)
    {
        await using var db = fixture.CreateDb(now);
        await using var transaction = await db.Database.BeginTransactionAsync();
        bookingLockRequested.TrySetResult();
        var booking = await Day22EventDatabase.CreateBookingRepository(db)
            .FindByIdForUpdateAsync(seeded.BookingId, CancellationToken.None);
        var action = await Day22EventDatabase.CreatePendingActionRepository(db)
            .GetActiveByBookingIdForUpdateAsync(seeded.BookingId, CancellationToken.None);
        if (booking is not null && action is { ResolvedAt: null })
        {
            booking.ChangePickup(replacementStationId, null);
            action.Resolve(BookingPendingActionResolved.ACCEPTED, now);
            await db.SaveChangesAsync();
        }

        await transaction.CommitAsync();
    }

    private async Task PassengerActionFirstExpiredAttemptAsync(
        SeededFallback seeded,
        DateTimeOffset now,
        TaskCompletionSource actionLocked,
        TaskCompletionSource requestBooking)
    {
        await using var db = fixture.CreateDb(now);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var actions = Day22EventDatabase.CreatePendingActionRepository(db);
        var action = await actions.GetByIdForUpdateAsync(seeded.ActionId, CancellationToken.None);
        action.Should().NotBeNull();
        actionLocked.TrySetResult();
        await requestBooking.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var booking = await Day22EventDatabase.CreateBookingRepository(db)
            .FindByIdForUpdateAsync(seeded.BookingId, CancellationToken.None);
        booking.Should().NotBeNull();
        action!.Deadline.Should().BeBefore(now);
        action.ResolvedAt.Should().BeNull();
        await transaction.CommitAsync();
    }
}
