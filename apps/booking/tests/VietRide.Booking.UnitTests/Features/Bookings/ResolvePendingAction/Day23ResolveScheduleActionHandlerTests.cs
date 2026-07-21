using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.Bookings.ResolvePendingAction;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.UnitTests.Features.Bookings.ResolvePendingAction;

public sealed class Day23ResolveScheduleActionHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-17T08:00:00Z");

    [Fact]
    public async Task AcceptedResolvesOnceAndPreservesConfirmedBooking()
    {
        var fixture = new Fixture("MEDIUM", 100_001);

        var result = await fixture.HandleAsync("ACCEPTED");

        result.ResolvedAction.Should().Be("ACCEPTED");
        fixture.Action.ResolvedAction.Should().Be(BookingPendingActionResolved.ACCEPTED);
        fixture.Booking.Status.Should().Be(BookingStatus.CONFIRMED);
        await fixture.History.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await fixture.Outbox.DidNotReceive().EnqueueAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectedUsesFrozenRefundCancelsAndEnqueuesCanonicalEvent()
    {
        var fixture = new Fixture("MEDIUM", 100_001);
        string? payload = null;
        fixture.Outbox.EnqueueAsync(
                Arg.Any<Guid>(),
                "booking.booking.cancelled",
                Arg.Do<string>(value => payload = value),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await fixture.HandleAsync("REJECTED");

        fixture.Booking.Status.Should().Be(BookingStatus.CANCELLED);
        fixture.Booking.CancellationReason.Should().Be(BookingCancellationReason.SCHEDULE_CHANGED);
        fixture.Booking.RefundOverride.Should().BeTrue();
        await fixture.History.Received(1).AddAsync(
            Arg.Is<BookingStatusHistory>(row => row.Status == BookingStatus.CANCELLED
                && row.ActorUserId == null
                && row.ReasonCode == "SCHEDULE_CHANGED"),
            Arg.Any<CancellationToken>());
        using var document = JsonDocument.Parse(payload!);
        document.RootElement.GetProperty("refundAmount").GetInt64().Should().Be(50_001);
        document.RootElement.GetProperty("refundOverride").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("eventId").GetGuid().Should().NotBeEmpty();
    }

    [Fact]
    public async Task StrictAfterCutoffReturnsExpired()
    {
        var fixture = new Fixture("MEDIUM", 100_000, Now.AddTicks(-1));

        var action = () => fixture.HandleAsync("ACCEPTED");

        (await action.Should().ThrowAsync<CodedConflictException>())
            .Which.ErrorCode.Should().Be("BOOKING_PENDING_ACTION_EXPIRED");
    }

    [Fact]
    public async Task NewKeyTerminalStateReturnsStableTerminalConflicts()
    {
        var fixture = new Fixture("MEDIUM", 100_000);
        fixture.Action.Resolve(BookingPendingActionResolved.SUPERSEDED, Now.AddMinutes(-1));

        var action = () => fixture.HandleAsync("ACCEPTED");

        (await action.Should().ThrowAsync<CodedConflictException>())
            .Which.ErrorCode.Should().Be("BOOKING_PENDING_ACTION_SUPERSEDED");
    }

    [Fact]
    public async Task AcceptedOrRejectedTerminalReturnsAlreadyResolved()
    {
        var fixture = new Fixture("MEDIUM", 100_000);
        fixture.Action.Resolve(BookingPendingActionResolved.ACCEPTED, Now.AddMinutes(-1));

        var act = () => fixture.HandleAsync("ACCEPTED");

        (await act.Should().ThrowAsync<CodedConflictException>())
            .Which.ErrorCode.Should().Be("BOOKING_PENDING_ACTION_ALREADY_RESOLVED");
    }

    [Theory]
    [InlineData(BookingPendingActionReason.ROUTE_CHANGE, BookingStatus.CONFIRMED)]
    [InlineData(BookingPendingActionReason.SCHEDULE_CHANGE, BookingStatus.CANCELLED)]
    public async Task WrongReasonOrBookingStateReturnsNotResolvable(
        BookingPendingActionReason reason,
        BookingStatus status)
    {
        var fixture = new Fixture("MEDIUM", 100_000);
        typeof(BookingPendingAction).GetProperty(nameof(BookingPendingAction.Reason))!.SetValue(fixture.Action, reason);
        typeof(BookingEntity).GetProperty(nameof(BookingEntity.Status))!.SetValue(fixture.Booking, status);

        var act = () => fixture.HandleAsync("ACCEPTED");

        (await act.Should().ThrowAsync<CodedConflictException>())
            .Which.ErrorCode.Should().Be("BOOKING_PENDING_ACTION_NOT_RESOLVABLE");
    }

    [Fact]
    public async Task MajorCutoffUsesLaterTerminalAndStrictAfterExpires()
    {
        var fixture = new Fixture("MAJOR", 100_000);
        fixture.Clock.UtcNow.Returns(Now.AddHours(2).AddTicks(1));

        var act = () => fixture.HandleAsync("ACCEPTED");

        (await act.Should().ThrowAsync<CodedConflictException>())
            .Which.ErrorCode.Should().Be("BOOKING_PENDING_ACTION_EXPIRED");
    }

    private sealed class Fixture
    {
        public Fixture(string severity, long totalAmount, DateTimeOffset? deadline = null)
        {
            Booking = CreateConfirmedBooking(totalAmount);
            var cutoff = deadline ?? Now.AddHours(1);
            var percent = severity == "MEDIUM" ? 50 : 100;
            var refund = (long)Math.Round(totalAmount * (percent / 100m), 0, MidpointRounding.AwayFromZero);
            Action = BookingPendingAction.Create(
                Booking.Id,
                BookingPendingActionReason.SCHEDULE_CHANGE,
                cutoff,
                Enum.Parse<BookingPendingActionSeverity>(severity),
                JsonSerializer.Serialize(new
                {
                    sourceEventId = Guid.NewGuid(),
                    oldDeparture = Now.AddHours(5),
                    newDeparture = Now.AddHours(8),
                    severity,
                    initialDeadline = cutoff,
                    terminalDeadline = severity == "MAJOR" ? Now.AddHours(2) : (DateTimeOffset?)null,
                    refundBasisAmount = totalAmount,
                    refundPercent = percent,
                    refundAmount = refund,
                }));
            PendingActions.GetByIdForUpdateAsync(Action.Id, Arg.Any<CancellationToken>()).Returns(Action);
            Bookings.FindByIdForUpdateAsync(Booking.Id, Arg.Any<CancellationToken>()).Returns(Booking);
            UnitOfWork.ExecuteInTransactionAsync(
                    Arg.Any<Func<Task<ResolvePendingActionResult>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<Func<Task<ResolvePendingActionResult>>>()());
            Clock.UtcNow.Returns(Now);
            Handler = new ResolvePendingActionCommandHandler(
                PendingActions, Bookings, History, Outbox, UnitOfWork, Clock);
        }

        public IBookingPendingActionRepository PendingActions { get; } = Substitute.For<IBookingPendingActionRepository>();
        public IBookingRepository Bookings { get; } = Substitute.For<IBookingRepository>();
        public IBookingStatusHistoryRepository History { get; } = Substitute.For<IBookingStatusHistoryRepository>();
        public IIntegrationEventOutbox Outbox { get; } = Substitute.For<IIntegrationEventOutbox>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public IClock Clock { get; } = Substitute.For<IClock>();
        public BookingEntity Booking { get; }
        public BookingPendingAction Action { get; }
        public ResolvePendingActionCommandHandler Handler { get; }

        public Task<ResolvePendingActionResult> HandleAsync(string action)
            => Handler.Handle(
                new ResolvePendingActionCommand(
                    Booking.Id,
                    Action.Id,
                    Booking.PassengerUserId,
                    Guid.NewGuid().ToString("D"),
                    action,
                    null,
                    []),
                CancellationToken.None);

        private static BookingEntity CreateConfirmedBooking(long amount)
        {
            var booking = BookingEntity.CreatePendingPayment(
                BookingCode.Generate(Now),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                null,
                null,
                Money.FromRaw(amount),
                Money.Zero,
                Money.FromRaw(amount),
                tripSnapshotDeparture: Now.AddHours(10));
            booking.Confirm(Now.AddHours(-1));
            return booking;
        }
    }
}
