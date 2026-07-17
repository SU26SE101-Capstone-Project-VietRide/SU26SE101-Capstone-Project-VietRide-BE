using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Jobs;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Application.Features.Bookings.HandleScheduleChange;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Booking.Infrastructure;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests.Messaging;

[Collection(VoucherPersistenceCollection.CollectionName)]
public sealed class Day23ScheduleProjectionCasIntegrationTests
    : IClassFixture<VoucherPersistenceIntegrationTests.DbBackedVoucherFactory>
{
    private static readonly DateTimeOffset OccurredAt = DateTimeOffset.Parse("2026-07-15T01:00:00Z");
    private static readonly DateTimeOffset OldDeparture = DateTimeOffset.Parse("2026-07-20T01:00:00Z");
    private readonly VoucherPersistenceIntegrationTests.DbBackedVoucherFactory _factory;

    public Day23ScheduleProjectionCasIntegrationTests(
        VoucherPersistenceIntegrationTests.DbBackedVoucherFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CurrentEqualsOldAdvancesEligibleRowsButCreatesFactAndActionOnlyForConfirmed()
    {
        await _factory.InitializeAsync();
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var pending = CreateBooking(tripId, operatorId, confirmed: false, totalAmount: 100_000);
        var confirmed = CreateBooking(tripId, operatorId, confirmed: true, totalAmount: 101);
        await SeedAsync(pending, confirmed);
        var superseded = BookingPendingAction.Create(
            confirmed.Id,
            BookingPendingActionReason.STOP_DISABLED,
            OccurredAt.AddHours(12),
            BookingPendingActionSeverity.MAJOR);
        await SeedActionAsync(superseded);
        IReadOnlyDictionary<Guid, DateTimeOffset> updatedAtBefore;
        await using (var beforeScope = _factory.Services.CreateAsyncScope())
        {
            var before = beforeScope.ServiceProvider.GetRequiredService<BookingDbContext>();
            updatedAtBefore = await before.Bookings.AsNoTracking()
                .Where(booking => booking.Id == pending.Id || booking.Id == confirmed.Id)
                .ToDictionaryAsync(booking => booking.Id, booking => booking.UpdatedAt);
        }

        var handlerNow = updatedAtBefore.Values.Max().AddMinutes(1);
        var scheduler = Substitute.For<IPendingActionRealertScheduler>();
        var command = CreateCommand(tripId, operatorId, OldDeparture.AddHours(3), "MEDIUM");

        _factory.SqlCapture.Clear();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var handler = CreateHandler(scope.ServiceProvider, scheduler, now: handlerNow);
            (await handler.Handle(command, CancellationToken.None)).Should().Be(1);
        }

        _factory.SqlCapture.Commands.Should().Contain(sql =>
            sql.Contains("ORDER BY id", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase));
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var rows = await verify.Bookings.AsNoTracking()
            .Where(booking => booking.Id == pending.Id || booking.Id == confirmed.Id)
            .OrderBy(booking => booking.Id)
            .ToListAsync();
        rows.Should().OnlyContain(booking => booking.TripSnapshotDeparture == OldDeparture);
        rows.Should().OnlyContain(booking => booking.TripCurrentDeparture == OldDeparture.AddHours(3));
        rows.Should().OnlyContain(booking => booking.UpdatedAt == handlerNow);
        rows.Should().OnlyContain(booking => booking.UpdatedAt > updatedAtBefore[booking.Id]);

        var actions = await verify.BookingPendingActions.AsNoTracking()
            .Where(row => row.BookingId == confirmed.Id)
            .ToListAsync();
        (await verify.BookingPendingActions.CountAsync(row => row.BookingId == pending.Id)).Should().Be(0);
        var action = actions.Single(row => row.ResolvedAt == null);
        action.BookingId.Should().Be(confirmed.Id);
        action.Reason.Should().Be(BookingPendingActionReason.SCHEDULE_CHANGE);
        action.Severity.Should().Be(BookingPendingActionSeverity.MEDIUM);
        actions.Single(row => row.Id == superseded.Id).ResolvedAction.Should()
            .Be(BookingPendingActionResolved.SUPERSEDED);
        using (var metadata = JsonDocument.Parse(action.Metadata!))
        {
            metadata.RootElement.GetProperty("refundBasisAmount").GetInt64().Should().Be(101);
            metadata.RootElement.GetProperty("refundPercent").GetInt32().Should().Be(50);
            metadata.RootElement.GetProperty("refundAmount").GetInt64().Should().Be(51);
        }

        var requiredFacts = await verify.OutboxEvents.AsNoTracking()
            .Where(row => row.EventType == BookingScheduleChangeRequiredIntegrationEvent.EventTypeValue)
            .ToListAsync();
        requiredFacts.Should().NotContain(row => HasBookingId(row.Payload, pending.Id));
        var fact = requiredFacts.Single(row => HasBookingId(row.Payload, confirmed.Id));
        using (var payload = JsonDocument.Parse(fact.Payload))
        {
            payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(fact.Id);
            payload.RootElement.GetProperty("bookingId").GetGuid().Should().Be(confirmed.Id);
            payload.RootElement.GetProperty("pendingActionId").GetGuid().Should().Be(action.Id);
        }

        scheduler.Received(1).EnsureScheduled(action.Id, OccurredAt.AddHours(2));
    }

    [Fact]
    public async Task CurrentEqualsNewIsDuplicateWithZeroDurableWrites()
    {
        await _factory.InitializeAsync();
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var newDeparture = OldDeparture.AddHours(2);
        var booking = CreateBooking(tripId, operatorId, confirmed: true, totalAmount: 100_000);
        SetCurrentDeparture(booking, newDeparture);
        await SeedAsync(booking);
        var scheduler = Substitute.For<IPendingActionRealertScheduler>();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var handler = CreateHandler(scope.ServiceProvider, scheduler);
            (await handler.Handle(
                CreateCommand(tripId, operatorId, newDeparture, "MINOR"),
                CancellationToken.None)).Should().Be(0);
        }

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var stored = await verify.Bookings.AsNoTracking().SingleAsync(row => row.Id == booking.Id);
        stored.TripSnapshotDeparture.Should().Be(OldDeparture);
        stored.TripCurrentDeparture.Should().Be(newDeparture);
        (await verify.BookingPendingActions.CountAsync(action => action.BookingId == booking.Id)).Should().Be(0);
        var duplicateFacts = await verify.OutboxEvents.AsNoTracking()
            .Where(row => row.EventType == BookingScheduleChangeInformationalIntegrationEvent.EventTypeValue)
            .ToListAsync();
        duplicateFacts.Should().NotContain(row => HasBookingId(row.Payload, booking.Id));
        scheduler.DidNotReceiveWithAnyArgs().EnsureScheduled(default, default);
    }

    [Fact]
    public async Task SeventhFractionalDigitAppliesOnceAndRedeliveryMatchesStoredProjection()
    {
        await _factory.InitializeAsync();
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var newDeparture = OldDeparture.AddHours(3).AddTicks(1);
        var booking = CreateBooking(tripId, operatorId, confirmed: true, totalAmount: 100_000);
        await SeedAsync(booking);
        var scheduler = Substitute.For<IPendingActionRealertScheduler>();
        var command = CreateCommand(tripId, operatorId, newDeparture, "MEDIUM");
        var handlerNow = OccurredAt.AddMinutes(5).AddTicks(1);

        await using (var firstScope = _factory.Services.CreateAsyncScope())
        {
            var handler = CreateHandler(firstScope.ServiceProvider, scheduler, now: handlerNow);
            (await handler.Handle(command, CancellationToken.None)).Should().Be(1);
        }

        await using (var replayScope = _factory.Services.CreateAsyncScope())
        {
            var handler = CreateHandler(replayScope.ServiceProvider, scheduler, now: handlerNow);
            (await handler.Handle(command, CancellationToken.None)).Should().Be(0);
        }

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var stored = await verify.Bookings.AsNoTracking().SingleAsync(row => row.Id == booking.Id);
        stored.TripSnapshotDeparture.Should().Be(OldDeparture);
        stored.TripCurrentDeparture.Should().Be(newDeparture.AddTicks(-1));
        stored.UpdatedAt.Should().Be(handlerNow.AddTicks(-1));

        var action = await verify.BookingPendingActions.AsNoTracking()
            .SingleAsync(row => row.BookingId == booking.Id
                && row.Reason == BookingPendingActionReason.SCHEDULE_CHANGE);
        var facts = await verify.OutboxEvents.AsNoTracking()
            .Where(row => row.EventType == BookingScheduleChangeRequiredIntegrationEvent.EventTypeValue)
            .ToListAsync();
        facts.Count(row => HasBookingId(row.Payload, booking.Id)).Should().Be(1);
        scheduler.Received(2).EnsureScheduled(action.Id, OccurredAt.AddHours(2));
    }

    [Fact]
    public async Task MinorFactUsesTheSamePayloadAndOutboxIdentityWithoutCreatingAnAction()
    {
        await _factory.InitializeAsync();
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var booking = CreateBooking(tripId, operatorId, confirmed: true, totalAmount: 100_000);
        await SeedAsync(booking);
        var scheduler = Substitute.For<IPendingActionRealertScheduler>();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var handler = CreateHandler(scope.ServiceProvider, scheduler);
            (await handler.Handle(
                CreateCommand(tripId, operatorId, OldDeparture.AddHours(2), "MINOR"),
                CancellationToken.None)).Should().Be(1);
        }

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var informationalFacts = await verify.OutboxEvents.AsNoTracking()
            .Where(row => row.EventType == BookingScheduleChangeInformationalIntegrationEvent.EventTypeValue)
            .ToListAsync();
        var fact = informationalFacts.Single(row => HasBookingId(row.Payload, booking.Id));
        using var payload = JsonDocument.Parse(fact.Payload);
        payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(fact.Id);
        payload.RootElement.GetProperty("bookingId").GetGuid().Should().Be(booking.Id);
        (await verify.BookingPendingActions.CountAsync(action => action.BookingId == booking.Id)).Should().Be(0);
        scheduler.DidNotReceiveWithAnyArgs().EnsureScheduled(default, default);
    }

    [Fact]
    public async Task ThirdProjectionValueFailsBeforeCommitAndLeavesWholeBatchUnchanged()
    {
        await _factory.InitializeAsync();
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var valid = CreateBooking(
            tripId,
            operatorId,
            confirmed: true,
            totalAmount: 100_000,
            id: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var conflict = CreateBooking(
            tripId,
            operatorId,
            confirmed: true,
            totalAmount: 100_000,
            id: Guid.Parse("00000000-0000-0000-0000-000000000002"));
        SetCurrentDeparture(conflict, OldDeparture.AddHours(1));
        await SeedAsync(valid, conflict);
        var scheduler = Substitute.For<IPendingActionRealertScheduler>();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var handler = CreateHandler(scope.ServiceProvider, scheduler);
            var act = () => handler.Handle(
                CreateCommand(tripId, operatorId, OldDeparture.AddHours(3), "MEDIUM"),
                CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*causal boundary*");
        }

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<BookingDbContext>();
        (await verify.Bookings.AsNoTracking().SingleAsync(row => row.Id == valid.Id))
            .TripCurrentDeparture.Should().Be(OldDeparture);
        (await verify.Bookings.AsNoTracking().SingleAsync(row => row.Id == conflict.Id))
            .TripCurrentDeparture.Should().Be(OldDeparture.AddHours(1));
        (await verify.BookingPendingActions.CountAsync(action =>
            action.BookingId == valid.Id || action.BookingId == conflict.Id)).Should().Be(0);
        var conflictFacts = await verify.OutboxEvents.AsNoTracking()
            .Where(row => row.EventType == BookingScheduleChangeRequiredIntegrationEvent.EventTypeValue)
            .ToListAsync();
        conflictFacts.Should().NotContain(row =>
            HasBookingId(row.Payload, valid.Id) || HasBookingId(row.Payload, conflict.Id));
        scheduler.DidNotReceiveWithAnyArgs().EnsureScheduled(default, default);
    }

    [Fact]
    public async Task SaveFailureRollsBackProjectionActionAndOutboxInOneTransaction()
    {
        await _factory.InitializeAsync();
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var booking = CreateBooking(tripId, operatorId, confirmed: true, totalAmount: 100_000);
        await SeedAsync(booking);
        var duplicateOutboxId = Guid.NewGuid();
        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var seed = seedScope.ServiceProvider.GetRequiredService<BookingDbContext>();
            seed.OutboxEvents.Add(new OutboxEvent
            {
                Id = duplicateOutboxId,
                EventType = "booking.test.existing",
                Payload = "{}",
                CreatedAt = OccurredAt,
            });
            await seed.SaveChangesAsync();
        }

        var scheduler = Substitute.For<IPendingActionRealertScheduler>();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            var failingOutbox = Substitute.For<IIntegrationEventOutbox>();
            failingOutbox.EnqueueAsync(
                    Arg.Any<Guid>(),
                    BookingScheduleChangeRequiredIntegrationEvent.EventTypeValue,
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    db.OutboxEvents.Add(new OutboxEvent
                    {
                        Id = duplicateOutboxId,
                        EventType = call.ArgAt<string>(1),
                        Payload = call.ArgAt<string>(2),
                        CreatedAt = OccurredAt,
                    });
                    return Task.CompletedTask;
                });
            var handler = CreateHandler(scope.ServiceProvider, scheduler, failingOutbox);
            var act = () => handler.Handle(
                CreateCommand(tripId, operatorId, OldDeparture.AddHours(3), "MEDIUM"),
                CancellationToken.None);
            await act.Should().ThrowAsync<DbUpdateException>();
        }

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var stored = await verify.Bookings.AsNoTracking().SingleAsync(row => row.Id == booking.Id);
        stored.TripSnapshotDeparture.Should().Be(OldDeparture);
        stored.TripCurrentDeparture.Should().Be(OldDeparture);
        (await verify.BookingPendingActions.CountAsync(action => action.BookingId == booking.Id)).Should().Be(0);
        var rollbackRows = await verify.OutboxEvents.AsNoTracking().ToListAsync();
        rollbackRows.Should().NotContain(row => HasBookingId(row.Payload, booking.Id));
        (await verify.OutboxEvents.CountAsync(row => row.Id == duplicateOutboxId)).Should().Be(1);
        scheduler.DidNotReceiveWithAnyArgs().EnsureScheduled(default, default);
    }

    private async Task SeedAsync(params BookingEntity[] bookings)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        db.Bookings.AddRange(bookings);
        await db.SaveChangesAsync();
    }

    private async Task SeedActionAsync(BookingPendingAction action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        db.BookingPendingActions.Add(action);
        await db.SaveChangesAsync();
    }

    private static HandleScheduleChangeCommandHandler CreateHandler(
        IServiceProvider services,
        IPendingActionRealertScheduler scheduler,
        IIntegrationEventOutbox? outbox = null,
        DateTimeOffset? now = null)
    {
        var db = services.GetRequiredService<BookingDbContext>();
        var clock = new FixedClock(now ?? OccurredAt.AddMinutes(5));
        return new HandleScheduleChangeCommandHandler(
            services.GetRequiredService<IBookingRepository>(),
            services.GetRequiredService<IBookingPendingActionRepository>(),
            outbox ?? new IntegrationEventOutbox(new OutboxStore(db, clock)),
            new EfUnitOfWork(db),
            scheduler,
            clock,
            Substitute.For<IScheduleChangeAutoAcceptScheduler>());
    }

    private static HandleScheduleChangeCommand CreateCommand(
        Guid tripId,
        Guid operatorId,
        DateTimeOffset newDeparture,
        string severity)
        => new(Guid.NewGuid(), OccurredAt, tripId, operatorId, OldDeparture, newDeparture, severity);

    private static BookingEntity CreateBooking(
        Guid tripId,
        Guid operatorId,
        bool confirmed,
        long totalAmount,
        Guid? id = null)
    {
        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(OccurredAt),
            Guid.NewGuid(),
            tripId,
            operatorId,
            Guid.NewGuid(),
            null,
            null,
            null,
            Money.FromRaw(totalAmount),
            Money.Zero,
            Money.FromRaw(totalAmount),
            tripSnapshotDeparture: OldDeparture);
        if (id.HasValue)
        {
            typeof(BookingEntity).GetProperty(nameof(BookingEntity.Id))!.SetValue(booking, id.Value);
        }

        if (confirmed)
        {
            booking.Confirm(OccurredAt.AddMinutes(-1));
        }

        return booking;
    }

    private static void SetCurrentDeparture(BookingEntity booking, DateTimeOffset departure)
        => typeof(BookingEntity).GetProperty(nameof(BookingEntity.TripCurrentDeparture))!
            .SetValue(booking, departure);

    private static bool HasBookingId(string payload, Guid bookingId)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.TryGetProperty("bookingId", out var value)
            && value.ValueKind == JsonValueKind.String
            && value.TryGetGuid(out var parsed)
            && parsed == bookingId;
    }
}
