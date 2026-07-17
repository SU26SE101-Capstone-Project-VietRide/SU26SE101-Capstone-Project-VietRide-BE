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

public sealed class HandleScheduleChangeCommandHandlerTests
{
    private static readonly DateTimeOffset OccurredAt = DateTimeOffset.Parse("2026-07-15T01:00:00Z");
    private static readonly Guid EventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TripId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OperatorId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task MinorPublishesExactInformationalFactWithoutActionOrSchedule()
    {
        var fixture = new Fixture();
        string? payload = null;
        fixture.Outbox.EnqueueAsync(
                Arg.Any<Guid>(),
                BookingScheduleChangeInformationalIntegrationEvent.EventTypeValue,
                Arg.Do<string>(value => payload = value),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var affected = await fixture.Handler.Handle(
            Command(OccurredAt.AddHours(2), "MINOR"),
            CancellationToken.None);

        affected.Should().Be(1);
        using var document = JsonDocument.Parse(payload!);
        document.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            ["eventId", "occurredAt", "bookingId", "tripId", "userId", "oldDeparture", "newDeparture", "severity"]);
        document.RootElement.GetProperty("severity").GetString().Should().Be("MINOR");
        await fixture.PendingActions.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        fixture.Scheduler.DidNotReceiveWithAnyArgs().EnsureScheduled(default, default);
    }

    [Theory]
    [InlineData(2, "MINOR")]
    [InlineData(2.0001, "MEDIUM")]
    [InlineData(5.9999, "MEDIUM")]
    [InlineData(6, "MAJOR")]
    public async Task SeverityBoundariesMatchTripProducer(double hours, string severity)
    {
        var fixture = new Fixture();

        var act = () => fixture.Handler.Handle(
            Command(OccurredAt.AddHours(hours), severity),
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CrossingIctDateRequiresMajor()
    {
        var fixture = new Fixture();
        var oldDeparture = DateTimeOffset.Parse("2026-07-15T16:30:00Z");
        var command = Command(DateTimeOffset.Parse("2026-07-15T17:30:00Z"), "MAJOR") with
        {
            OldDeparture = oldDeparture,
        };
        SetCurrentDeparture(fixture.Booking, oldDeparture);

        await fixture.Handler.Handle(command, CancellationToken.None);
    }

    [Theory]
    [InlineData(30, 24)]
    [InlineData(24.0001, 22.0001)]
    [InlineData(24, 23.5)]
    [InlineData(23.9999, 23.4999)]
    [InlineData(0.5, 1)]
    public void DeadlineCoversBothBranchesAndTwentyFourHourBoundary(
        double departureHours,
        double expectedDeadlineHours)
    {
        HandleScheduleChangeCommandHandler.CalculateDeadline(
                OccurredAt,
                OccurredAt.AddHours(departureHours))
            .Should().BeCloseTo(OccurredAt.AddHours(expectedDeadlineHours), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task MediumCreatesExactActionAndOutboxThenSchedulesAfterCommit()
    {
        var fixture = new Fixture();
        var calls = new List<string>();
        fixture.UnitOfWork.ExecuteInTransactionAsync(
                Arg.Any<Func<Task<int>>>(),
                Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var result = await call.Arg<Func<Task<int>>>()();
                calls.Add("commit");
                return result;
            });
        fixture.Scheduler.When(value => value.EnsureScheduled(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>()))
            .Do(_ => calls.Add("schedule"));
        BookingPendingAction? captured = null;
        fixture.PendingActions.AddAsync(
                Arg.Do<BookingPendingAction>(action => captured = action),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<BookingPendingAction>());

        await fixture.Handler.Handle(Command(OccurredAt.AddHours(3), "MEDIUM"), CancellationToken.None);

        calls.Should().Equal("commit", "schedule");
        captured.Should().NotBeNull();
        captured!.Reason.Should().Be(BookingPendingActionReason.SCHEDULE_CHANGE);
        captured.Severity.Should().Be(BookingPendingActionSeverity.MEDIUM);
        using var metadata = JsonDocument.Parse(captured.Metadata!);
        metadata.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            [
                "sourceEventId", "oldDeparture", "newDeparture", "severity", "initialDeadline",
                "terminalDeadline", "refundBasisAmount", "refundPercent", "refundAmount",
            ]);
        metadata.RootElement.GetProperty("terminalDeadline").ValueKind.Should().Be(JsonValueKind.Null);
        metadata.RootElement.GetProperty("refundBasisAmount").GetInt64().Should().Be(100_000);
        metadata.RootElement.GetProperty("refundPercent").GetInt32().Should().Be(50);
        metadata.RootElement.GetProperty("refundAmount").GetInt64().Should().Be(50_000);
        fixture.Scheduler.Received(1).EnsureScheduled(captured.Id, OccurredAt.AddHours(2));
        await fixture.Outbox.Received(1).EnqueueAsync(
            Arg.Any<Guid>(),
            BookingScheduleChangeRequiredIntegrationEvent.EventTypeValue,
            Arg.Is<string>(value => HasMandatoryRequiredFields(value, captured.Id)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReplayRepairsLogicalScheduleWithoutDuplicateActionOrInitialOutbox()
    {
        var fixture = new Fixture();
        var existing = BookingPendingAction.Create(
            fixture.Booking.Id,
            BookingPendingActionReason.SCHEDULE_CHANGE,
            OccurredAt.AddHours(4),
            BookingPendingActionSeverity.MAJOR,
            JsonSerializer.Serialize(new { sourceEventId = EventId }));
        fixture.PendingActions.GetByBookingAndSourceEventAsync(
                fixture.Booking.Id,
                EventId,
                Arg.Any<CancellationToken>())
            .Returns([existing]);
        SetCurrentDeparture(fixture.Booking, OccurredAt.AddHours(6));

        (await fixture.Handler.Handle(Command(OccurredAt.AddHours(6), "MAJOR"), CancellationToken.None))
            .Should().Be(0);

        await fixture.PendingActions.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await fixture.Outbox.DidNotReceive().EnqueueAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        fixture.Scheduler.Received(1).EnsureScheduled(existing.Id, OccurredAt.AddHours(2));
    }

    [Fact]
    public async Task OnlyRepositoryConfirmedRowsAreEligible()
    {
        var fixture = new Fixture();
        fixture.Bookings.GetScheduleChangeBookingsForUpdateAsync(
            TripId, OperatorId, Arg.Any<CancellationToken>()).Returns([]);

        (await fixture.Handler.Handle(Command(OccurredAt.AddHours(2), "MINOR"), CancellationToken.None))
            .Should().Be(0);

        await fixture.Outbox.DidNotReceive().EnqueueAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void InformationalAndRequiredContractsRejectWrongSeverity()
    {
        var createInfo = () => new BookingScheduleChangeInformationalIntegrationEvent(
            Guid.NewGuid(), OccurredAt, Guid.NewGuid(), TripId, Guid.NewGuid(), OccurredAt, OccurredAt.AddHours(3), "MEDIUM");
        var createRequired = () => new BookingScheduleChangeRequiredIntegrationEvent(
            Guid.NewGuid(), OccurredAt, Guid.NewGuid(), TripId, Guid.NewGuid(), Guid.NewGuid(),
            OccurredAt.AddHours(1), OccurredAt, OccurredAt.AddHours(1), "MINOR");

        createInfo.Should().Throw<ArgumentException>();
        createRequired.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DeadlineEqualityIsNotExpired()
    {
        var action = BookingPendingAction.Create(
            Guid.NewGuid(), BookingPendingActionReason.SCHEDULE_CHANGE, OccurredAt,
            BookingPendingActionSeverity.MEDIUM);

        action.IsDeadlineExpired(OccurredAt).Should().BeFalse();
        action.IsDeadlineExpired(OccurredAt.AddTicks(1)).Should().BeTrue();
    }

    private static HandleScheduleChangeCommand Command(DateTimeOffset newDeparture, string severity)
        => new(EventId, OccurredAt, TripId, OperatorId, OccurredAt, newDeparture, severity);

    private static bool HasMandatoryRequiredFields(string payload, Guid pendingActionId)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        return root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal).SetEquals(
                ["eventId", "occurredAt", "bookingId", "tripId", "userId", "pendingActionId", "deadline", "oldDeparture", "newDeparture", "severity"])
            && root.GetProperty("pendingActionId").GetGuid() == pendingActionId
            && root.GetProperty("severity").GetString() == "MEDIUM";
    }

    private static BookingEntity CreateBooking()
    {
        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(OccurredAt), Guid.NewGuid(), TripId, OperatorId, Guid.NewGuid(),
            null, null, null, Money.FromRaw(100_000), Money.Zero, Money.FromRaw(100_000),
            tripSnapshotDeparture: OccurredAt);
        typeof(BookingEntity).GetProperty(nameof(BookingEntity.Status))!.SetValue(booking, BookingStatus.CONFIRMED);
        return booking;
    }

    private static void SetCurrentDeparture(BookingEntity booking, DateTimeOffset departure)
        => typeof(BookingEntity).GetProperty(nameof(BookingEntity.TripCurrentDeparture))!
            .SetValue(booking, departure);

    private sealed class Fixture
    {
        public Fixture()
        {
            Booking = CreateBooking();
            Bookings.AcquireEventLockAsync(EventId, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            Bookings.GetScheduleChangeBookingsForUpdateAsync(
                TripId, OperatorId, Arg.Any<CancellationToken>()).Returns([Booking]);
            Bookings.TryAdvanceTripCurrentDepartureAsync(
                Booking.Id,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>()).Returns(true);
            Bookings.HasOutboxEventAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
            PendingActions.GetByBookingAndSourceEventAsync(Booking.Id, EventId, Arg.Any<CancellationToken>()).Returns([]);
            PendingActions.GetActiveByBookingIdAsync(Booking.Id, Arg.Any<CancellationToken>()).Returns((BookingPendingAction?)null);
            UnitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<Task<int>>>(), Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<Func<Task<int>>>()());
            Clock.UtcNow.Returns(OccurredAt.AddMinutes(5));
            Handler = new HandleScheduleChangeCommandHandler(
                Bookings, PendingActions, Outbox, UnitOfWork, Scheduler, Clock);
        }

        public IBookingRepository Bookings { get; } = Substitute.For<IBookingRepository>();
        public IBookingPendingActionRepository PendingActions { get; } = Substitute.For<IBookingPendingActionRepository>();
        public IIntegrationEventOutbox Outbox { get; } = Substitute.For<IIntegrationEventOutbox>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public IPendingActionRealertScheduler Scheduler { get; } = Substitute.For<IPendingActionRealertScheduler>();
        public IClock Clock { get; } = Substitute.For<IClock>();
        public BookingEntity Booking { get; }
        public HandleScheduleChangeCommandHandler Handler { get; }
    }
}
