using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Jobs;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Application.Features.Bookings.HandleScheduleChange;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.UnitTests.Features.Bookings;

public sealed class Day23ScheduleProjectionRulesTests
{
    private static readonly Guid EventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TripId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OperatorId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset OccurredAt = DateTimeOffset.Parse("2026-07-15T01:00:00Z");
    private static readonly DateTimeOffset OldDeparture = DateTimeOffset.Parse("2026-07-20T01:00:00Z");

    [Fact]
    public async Task EligibleStatusesAdvanceProjectionButOnlyConfirmedEmitsInformationalFact()
    {
        var pending = CreateBooking(100_000, confirmed: false);
        var confirmed = CreateBooking(100_000, confirmed: true);
        var fixture = new Fixture([pending, confirmed]);
        Guid persistedEventId = Guid.Empty;
        string? payload = null;
        fixture.Outbox.EnqueueAsync(
                Arg.Do<Guid>(value => persistedEventId = value),
                BookingScheduleChangeInformationalIntegrationEvent.EventTypeValue,
                Arg.Do<string>(value => payload = value),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var affected = await fixture.Handler.Handle(
            Command(OldDeparture.AddHours(2), "MINOR"),
            CancellationToken.None);

        affected.Should().Be(1);
        await fixture.Bookings.Received(1).TryAdvanceTripCurrentDepartureAsync(
            pending.Id,
            OldDeparture,
            OldDeparture.AddHours(2),
            OccurredAt.AddMinutes(5),
            Arg.Any<CancellationToken>());
        await fixture.Bookings.Received(1).TryAdvanceTripCurrentDepartureAsync(
            confirmed.Id,
            OldDeparture,
            OldDeparture.AddHours(2),
            OccurredAt.AddMinutes(5),
            Arg.Any<CancellationToken>());
        using var document = JsonDocument.Parse(payload!);
        persistedEventId.Should().NotBeEmpty();
        document.RootElement.GetProperty("eventId").GetGuid().Should().Be(persistedEventId);
        document.RootElement.GetProperty("bookingId").GetGuid().Should().Be(confirmed.Id);
        await fixture.PendingActions.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        fixture.Scheduler.DidNotReceiveWithAnyArgs().EnsureScheduled(default, default);
        await fixture.Outbox.DidNotReceive().EnqueueAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("MEDIUM", 3, 50, 51, false)]
    [InlineData("MAJOR", 6, 100, 101, true)]
    public async Task RequiredActionFreezesExactRefundAndDeadlineMetadata(
        string severity,
        int deltaHours,
        int expectedPercent,
        long expectedRefund,
        bool hasTerminalDeadline)
    {
        var booking = CreateBooking(101, confirmed: true);
        var fixture = new Fixture([booking]);
        BookingPendingAction? action = null;
        Guid persistedEventId = Guid.Empty;
        string? payload = null;
        fixture.PendingActions.AddAsync(
                Arg.Do<BookingPendingAction>(value => action = value),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<BookingPendingAction>());
        fixture.Outbox.EnqueueAsync(
                Arg.Do<Guid>(value => persistedEventId = value),
                BookingScheduleChangeRequiredIntegrationEvent.EventTypeValue,
                Arg.Do<string>(value => payload = value),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var newDeparture = OldDeparture.AddHours(deltaHours);

        await fixture.Handler.Handle(Command(newDeparture, severity), CancellationToken.None);

        action.Should().NotBeNull();
        action!.Reason.Should().Be(BookingPendingActionReason.SCHEDULE_CHANGE);
        action.Severity.Should().Be(Enum.Parse<BookingPendingActionSeverity>(severity));
        using var metadata = JsonDocument.Parse(action.Metadata!);
        metadata.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            [
                "sourceEventId", "oldDeparture", "newDeparture", "severity", "initialDeadline",
                "terminalDeadline", "refundBasisAmount", "refundPercent", "refundAmount",
            ]);
        metadata.RootElement.GetProperty("sourceEventId").GetGuid().Should().Be(EventId);
        metadata.RootElement.GetProperty("initialDeadline").GetDateTimeOffset().Should().Be(action.Deadline);
        metadata.RootElement.GetProperty("refundBasisAmount").GetInt64().Should().Be(101);
        metadata.RootElement.GetProperty("refundPercent").GetInt32().Should().Be(expectedPercent);
        metadata.RootElement.GetProperty("refundAmount").GetInt64().Should().Be(expectedRefund);
        if (hasTerminalDeadline)
        {
            metadata.RootElement.GetProperty("terminalDeadline").GetDateTimeOffset().Should()
                .Be(newDeparture.AddMinutes(-30));
        }
        else
        {
            metadata.RootElement.GetProperty("terminalDeadline").ValueKind.Should().Be(JsonValueKind.Null);
        }

        using var required = JsonDocument.Parse(payload!);
        required.RootElement.GetProperty("eventId").GetGuid().Should().Be(persistedEventId);
        required.RootElement.GetProperty("pendingActionId").GetGuid().Should().Be(action.Id);
        required.RootElement.GetProperty("deadline").GetDateTimeOffset().Should().Be(action.Deadline);
        fixture.Scheduler.Received(1).EnsureScheduled(action.Id, OccurredAt.AddHours(2));
    }

    [Fact]
    public async Task ThirdProjectionValueFailsPreflightBeforeAnyWrite()
    {
        var valid = CreateBooking(100_000, confirmed: true);
        var conflict = CreateBooking(100_000, confirmed: true);
        SetCurrentDeparture(conflict, OldDeparture.AddHours(1));
        var fixture = new Fixture([valid, conflict]);

        var act = () => fixture.Handler.Handle(
            Command(OldDeparture.AddHours(3), "MEDIUM"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*causal boundary*");
        await fixture.Bookings.DidNotReceiveWithAnyArgs().TryAdvanceTripCurrentDepartureAsync(
            default, default, default, default, default);
        await fixture.PendingActions.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await fixture.Outbox.DidNotReceive().EnqueueAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        fixture.Scheduler.DidNotReceiveWithAnyArgs().EnsureScheduled(default, default);
    }

    [Fact]
    public async Task CurrentEqualsNewIsDurableNoOpButRepairsExistingSchedule()
    {
        var newDeparture = OldDeparture.AddHours(3);
        var booking = CreateBooking(100_000, confirmed: true);
        SetCurrentDeparture(booking, newDeparture);
        var fixture = new Fixture([booking]);
        var action = BookingPendingAction.Create(
            booking.Id,
            BookingPendingActionReason.SCHEDULE_CHANGE,
            OccurredAt.AddHours(24),
            BookingPendingActionSeverity.MEDIUM,
            JsonSerializer.Serialize(new { sourceEventId = EventId }));
        fixture.PendingActions.GetByBookingAndSourceEventAsync(
                booking.Id,
                EventId,
                Arg.Any<CancellationToken>())
            .Returns([action]);

        (await fixture.Handler.Handle(Command(newDeparture, "MEDIUM"), CancellationToken.None))
            .Should().Be(0);

        await fixture.Bookings.DidNotReceiveWithAnyArgs().TryAdvanceTripCurrentDepartureAsync(
            default, default, default, default, default);
        await fixture.PendingActions.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await fixture.Outbox.DidNotReceive().EnqueueAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        fixture.Scheduler.Received(1).EnsureScheduled(action.Id, OccurredAt.AddHours(2));
    }

    private static HandleScheduleChangeCommand Command(DateTimeOffset newDeparture, string severity)
        => new(EventId, OccurredAt, TripId, OperatorId, OldDeparture, newDeparture, severity);

    private static BookingEntity CreateBooking(long totalAmount, bool confirmed)
    {
        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(OccurredAt),
            Guid.NewGuid(),
            TripId,
            OperatorId,
            Guid.NewGuid(),
            null,
            null,
            null,
            Money.FromRaw(totalAmount),
            Money.Zero,
            Money.FromRaw(totalAmount),
            tripSnapshotDeparture: OldDeparture);
        if (confirmed)
        {
            booking.Confirm(OccurredAt.AddMinutes(-1));
        }

        return booking;
    }

    private static void SetCurrentDeparture(BookingEntity booking, DateTimeOffset departure)
        => typeof(BookingEntity).GetProperty(nameof(BookingEntity.TripCurrentDeparture))!
            .SetValue(booking, departure);

    private sealed class Fixture
    {
        public Fixture(IReadOnlyList<BookingEntity> candidates)
        {
            Bookings.AcquireEventLockAsync(EventId, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            Bookings.GetScheduleChangeBookingsForUpdateAsync(
                TripId, OperatorId, Arg.Any<CancellationToken>()).Returns(candidates);
            Bookings.TryAdvanceTripCurrentDepartureAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<DateTimeOffset>(),
                    Arg.Any<DateTimeOffset>(),
                    Arg.Any<DateTimeOffset>(),
                    Arg.Any<CancellationToken>())
                .Returns(true);
            PendingActions.GetByBookingAndSourceEventAsync(
                    Arg.Any<Guid>(), EventId, Arg.Any<CancellationToken>())
                .Returns([]);
            PendingActions.GetActiveByBookingIdAsync(
                    Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns((BookingPendingAction?)null);
            Outbox.EnqueueAsync(
                    Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            UnitOfWork.ExecuteInTransactionAsync(
                    Arg.Any<Func<Task<int>>>(), Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<Func<Task<int>>>()());
            Clock.UtcNow.Returns(OccurredAt.AddMinutes(5));
            Handler = new HandleScheduleChangeCommandHandler(
                Bookings, PendingActions, Outbox, UnitOfWork, Scheduler, Clock, AutoAcceptScheduler);
        }

        public IBookingRepository Bookings { get; } = Substitute.For<IBookingRepository>();
        public IBookingPendingActionRepository PendingActions { get; }
            = Substitute.For<IBookingPendingActionRepository>();
        public IIntegrationEventOutbox Outbox { get; } = Substitute.For<IIntegrationEventOutbox>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public IPendingActionRealertScheduler Scheduler { get; }
            = Substitute.For<IPendingActionRealertScheduler>();
        public IScheduleChangeAutoAcceptScheduler AutoAcceptScheduler { get; }
            = Substitute.For<IScheduleChangeAutoAcceptScheduler>();
        public IClock Clock { get; } = Substitute.For<IClock>();
        public HandleScheduleChangeCommandHandler Handler { get; }
    }
}
