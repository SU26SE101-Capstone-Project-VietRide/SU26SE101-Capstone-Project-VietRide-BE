using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Jobs;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Application.Features.Bookings.HandleVehicleSwap;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.UnitTests.Features.Bookings;

public sealed class HandleVehicleSwapCommandHandlerTests
{
    private static readonly DateTimeOffset OccurredAt = DateTimeOffset.Parse("2026-07-15T02:00:00Z");
    private static readonly Guid EventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TripId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OperatorId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task ExactImpact_CommitsActionAndOutboxBeforeScheduling()
    {
        var fixture = new Fixture(OccurredAt.AddMinutes(5));
        var calls = new List<string>();
        fixture.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => { calls.Add("save"); return 1; });
        fixture.Scheduler.When(scheduler => scheduler.EnsureScheduled(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>()))
            .Do(_ => calls.Add("schedule"));

        var created = await fixture.Handler.Handle(CreateCommand(), CancellationToken.None);

        created.Should().Be(1);
        calls.Should().Equal("save", "schedule");
        await fixture.PendingActions.Received(1).AddAsync(
            Arg.Is<BookingPendingAction>(action =>
                action.Reason == BookingPendingActionReason.PENDING_SEAT_ASSIGNMENT
                && action.Severity == null
                && action.Deadline == OccurredAt.AddHours(4)
                && MetadataIsExact(action.Metadata)),
            Arg.Any<CancellationToken>());
        await fixture.Outbox.Received(1).EnqueueAsync(
            "booking.booking.seat_reassignment_required",
            Arg.Is<string>(payload => PayloadIsExact(payload, fixture.Booking.Id)),
            Arg.Any<CancellationToken>());
        fixture.Scheduler.Received(1).EnsureScheduled(Arg.Any<Guid>(), OccurredAt.AddHours(2));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    public async Task DeadlineMustBeStrictlyAfterHandlerClock(int secondsFromDeadline, int expectedCreated)
    {
        var deadline = OccurredAt.AddHours(4);
        var fixture = new Fixture(deadline.AddSeconds(-secondsFromDeadline));

        var created = await fixture.Handler.Handle(CreateCommand(), CancellationToken.None);

        created.Should().Be(expectedCreated);
    }

    [Fact]
    public async Task ReplayFindsExistingActionAndRepairsScheduleWithoutDuplicateOutbox()
    {
        var fixture = new Fixture(OccurredAt.AddMinutes(10));
        var existing = BookingPendingAction.Create(
            fixture.Booking.Id,
            BookingPendingActionReason.PENDING_SEAT_ASSIGNMENT,
            OccurredAt.AddHours(4),
            metadata: JsonSerializer.Serialize(new
            {
                sourceEventId = EventId,
                seatNumbers = new[] { "A01" },
                reason = "SEAT_REMOVED",
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        fixture.PendingActions.GetByBookingAndSourceEventAsync(
                fixture.Booking.Id, EventId, Arg.Any<CancellationToken>())
            .Returns([existing]);

        var created = await fixture.Handler.Handle(CreateCommand(), CancellationToken.None);

        created.Should().Be(0);
        await fixture.PendingActions.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await fixture.Outbox.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default!, default);
        await fixture.UnitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
        fixture.Scheduler.Received(1).EnsureScheduled(existing.Id, OccurredAt.AddHours(2));
    }

    [Fact]
    public async Task SchedulerFailurePropagatesAfterCommitForDlqReplay()
    {
        var fixture = new Fixture(OccurredAt.AddMinutes(10));
        fixture.Scheduler.When(scheduler => scheduler.EnsureScheduled(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>()))
            .Do(_ => throw new InvalidOperationException("hangfire unavailable"));

        var act = () => fixture.Handler.Handle(CreateCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(BookingStatus.PENDING_PAYMENT)]
    [InlineData(BookingStatus.COMPLETED)]
    [InlineData(BookingStatus.EXPIRED)]
    [InlineData(BookingStatus.CANCELLED)]
    [InlineData(BookingStatus.NO_SHOW)]
    [InlineData(BookingStatus.PARTIAL_NO_SHOW)]
    [InlineData(BookingStatus.REFUNDED)]
    [InlineData(BookingStatus.DISRUPTED)]
    public async Task OnlyConfirmedBookingIsEligible(BookingStatus status)
    {
        var fixture = new Fixture(OccurredAt.AddMinutes(5), status);

        var created = await fixture.Handler.Handle(CreateCommand(), CancellationToken.None);

        created.Should().Be(0);
        await fixture.PendingActions.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await fixture.Outbox.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default!, default);
    }

    [Fact]
    public async Task ExactBookingTripAndOperatorMustAllMatch()
    {
        var fixture = new Fixture(OccurredAt.AddMinutes(5));
        var wrongOperator = CreateCommand() with { OperatorId = Guid.NewGuid() };
        var wrongTrip = CreateCommand() with { TripId = Guid.NewGuid() };

        (await fixture.Handler.Handle(wrongOperator, CancellationToken.None)).Should().Be(0);
        (await fixture.Handler.Handle(wrongTrip, CancellationToken.None)).Should().Be(0);

        await fixture.PendingActions.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await fixture.Outbox.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default!, default);
    }

    [Fact]
    public async Task DuplicateAndReorderedImpactIsNormalizedToOneActionAndOneOutbox()
    {
        var fixture = new Fixture(OccurredAt.AddMinutes(5));
        var impact = CreateCommand().SeatImpacts.Single();
        var command = CreateCommand() with
        {
            SeatImpacts =
            [
                impact with { SeatNumbers = ["A02", "a01"] },
                impact with { SeatNumbers = [" a01 ", "A02", "A01"] },
            ],
        };

        (await fixture.Handler.Handle(command, CancellationToken.None)).Should().Be(1);

        await fixture.PendingActions.Received(1).AddAsync(Arg.Any<BookingPendingAction>(), Arg.Any<CancellationToken>());
        await fixture.Outbox.Received(1).EnqueueAsync(
            BookingSeatReassignmentRequiredIntegrationEvent.EventTypeValue,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DepartureCutoffWinsAndIsPersistedExactly()
    {
        var fixture = new Fixture(OccurredAt.AddMinutes(5));
        var departure = OccurredAt.AddHours(2);

        (await fixture.Handler.Handle(CreateCommand() with { DepartureDateTime = departure }, CancellationToken.None))
            .Should().Be(1);

        await fixture.PendingActions.Received(1).AddAsync(
            Arg.Is<BookingPendingAction>(action => action.Deadline == departure.AddMinutes(-30)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailedAtomicCommitDoesNotSchedule()
    {
        var fixture = new Fixture(OccurredAt.AddMinutes(5));
        fixture.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("commit rolled back"));

        var act = () => fixture.Handler.Handle(CreateCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.Scheduler.DidNotReceiveWithAnyArgs().EnsureScheduled(default, default);
    }

    [Fact]
    public async Task CrashAfterCommitThenDlqReplayRepairsScheduleWithoutDuplicatingDurableWrites()
    {
        var fixture = new Fixture(OccurredAt.AddMinutes(5));
        BookingPendingAction? persisted = null;
        fixture.PendingActions.AddAsync(Arg.Do<BookingPendingAction>(action => persisted = action),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<BookingPendingAction>());
        var scheduleAttempts = 0;
        fixture.Scheduler.When(scheduler => scheduler.EnsureScheduled(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>()))
            .Do(_ =>
            {
                if (++scheduleAttempts == 1)
                {
                    throw new InvalidOperationException("crash after commit");
                }
            });

        await FluentActions.Awaiting(() => fixture.Handler.Handle(CreateCommand(), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        fixture.PendingActions.GetByBookingAndSourceEventAsync(
                fixture.Booking.Id, EventId, Arg.Any<CancellationToken>())
            .Returns(_ => [persisted!]);

        (await fixture.Handler.Handle(CreateCommand(), CancellationToken.None)).Should().Be(0);

        await fixture.PendingActions.Received(1).AddAsync(Arg.Any<BookingPendingAction>(), Arg.Any<CancellationToken>());
        await fixture.Outbox.Received(1).EnqueueAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        fixture.Scheduler.Received(2).EnsureScheduled(persisted!.Id, OccurredAt.AddHours(2));
    }

    [Fact]
    public async Task MultipleExactImpactsCreateIndependentActions()
    {
        var fixture = new Fixture(OccurredAt.AddMinutes(5));
        var secondId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var second = CreateBooking(secondId, BookingStatus.CONFIRMED);
        fixture.Bookings.QueryNoTracking().Returns(new[] { fixture.Booking, second }.AsQueryable());
        fixture.PendingActions.GetByBookingAndSourceEventAsync(secondId, EventId, Arg.Any<CancellationToken>())
            .Returns([]);
        fixture.PendingActions.GetActiveByBookingIdAsync(secondId, Arg.Any<CancellationToken>())
            .Returns((BookingPendingAction?)null);
        var command = CreateCommand() with
        {
            SeatImpacts =
            [
                CreateCommand().SeatImpacts.Single(),
                new VehicleSwapSeatImpact(secondId, ["B01"], "SEAT_TYPE_DOWNGRADED"),
            ],
        };

        (await fixture.Handler.Handle(command, CancellationToken.None)).Should().Be(2);

        await fixture.PendingActions.Received(2).AddAsync(Arg.Any<BookingPendingAction>(), Arg.Any<CancellationToken>());
        await fixture.Outbox.Received(2).EnqueueAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        fixture.Scheduler.Received(2).EnsureScheduled(Arg.Any<Guid>(), OccurredAt.AddHours(2));
    }

    private static HandleVehicleSwapCommand CreateCommand()
        => new(EventId, OccurredAt, TripId, OperatorId, OccurredAt.AddHours(8),
            [new VehicleSwapSeatImpact(Guid.Parse("33333333-3333-3333-3333-333333333333"), [" a01 "], "SEAT_REMOVED")]);

    private static bool MetadataIsExact(string? metadata)
    {
        using var document = JsonDocument.Parse(metadata!);
        return document.RootElement.GetProperty("sourceEventId").GetGuid() == EventId
            && document.RootElement.GetProperty("reason").GetString() == "SEAT_REMOVED"
            && document.RootElement.GetProperty("seatNumbers").EnumerateArray().Single().GetString() == "A01";
    }

    private static bool PayloadIsExact(string payload, Guid bookingId)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        return root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal)
                .SetEquals(["eventId", "occurredAt", "bookingId", "tripId", "userId", "pendingActionId", "deadline", "seatNumbers", "reason"])
            && root.GetProperty("bookingId").GetGuid() == bookingId
            && root.GetProperty("tripId").GetGuid() == TripId
            && root.GetProperty("reason").GetString() == "SEAT_REMOVED"
            && root.GetProperty("seatNumbers").EnumerateArray().Single().GetString() == "A01";
    }

    private static BookingEntity CreateBooking(Guid id, BookingStatus status)
    {
        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(OccurredAt), Guid.NewGuid(), TripId, OperatorId, Guid.NewGuid(),
            null, null, null, Money.FromRaw(100_000), Money.Zero, Money.FromRaw(100_000));
        typeof(BookingEntity).GetProperty(nameof(BookingEntity.Id))!.SetValue(booking, id);
        typeof(BookingEntity).GetProperty(nameof(BookingEntity.Status))!.SetValue(booking, status);
        return booking;
    }

    private sealed class Fixture
    {
        public Fixture(DateTimeOffset now, BookingStatus status = BookingStatus.CONFIRMED)
        {
            Booking = CreateBooking(Guid.Parse("33333333-3333-3333-3333-333333333333"), status);
            Bookings.QueryNoTracking().Returns(new[] { Booking }.AsQueryable());
            PendingActions.GetByBookingAndSourceEventAsync(
                    Booking.Id, EventId, Arg.Any<CancellationToken>())
                .Returns([]);
            PendingActions.GetActiveByBookingIdAsync(Booking.Id, Arg.Any<CancellationToken>())
                .Returns((BookingPendingAction?)null);
            Clock.UtcNow.Returns(now);
            UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
            Handler = new HandleVehicleSwapCommandHandler(
                Bookings, PendingActions, Outbox, UnitOfWork, Scheduler, Clock);
        }

        public IBookingRepository Bookings { get; } = Substitute.For<IBookingRepository>();
        public IBookingPendingActionRepository PendingActions { get; } = Substitute.For<IBookingPendingActionRepository>();
        public IIntegrationEventOutbox Outbox { get; } = Substitute.For<IIntegrationEventOutbox>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public IPendingActionRealertScheduler Scheduler { get; } = Substitute.For<IPendingActionRealertScheduler>();
        public IClock Clock { get; } = Substitute.For<IClock>();
        public BookingEntity Booking { get; }
        public HandleVehicleSwapCommandHandler Handler { get; }
    }
}
