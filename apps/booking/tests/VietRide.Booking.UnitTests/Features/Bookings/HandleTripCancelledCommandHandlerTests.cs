using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.Bookings.HandleTripCancelled;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.UnitTests.Features.Bookings;

public sealed class HandleTripCancelledCommandHandlerTests
{
    private static readonly DateTimeOffset OccurredAt = DateTimeOffset.Parse("2026-07-15T01:00:00Z");
    private static readonly Guid TripId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OperatorId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task PendingPaymentAndConfirmedCancelAtomicallyWithExactRefundsAndHistory()
    {
        var pending = CreateBooking(BookingStatus.PENDING_PAYMENT, 80_000);
        var confirmed = CreateBooking(BookingStatus.CONFIRMED, 125_001);
        var fixture = new Fixture([pending, confirmed]);
        var payloads = new List<string>();
        fixture.Outbox.EnqueueAsync(
                "booking.booking.cancelled",
                Arg.Do<string>(payloads.Add),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var affected = await fixture.Handler.Handle(Command(), CancellationToken.None);

        affected.Should().Be(2);
        pending.Status.Should().Be(BookingStatus.CANCELLED);
        confirmed.Status.Should().Be(BookingStatus.CANCELLED);
        pending.CancellationReason.Should().Be(BookingCancellationReason.OPERATOR_CANCELLED_TRIP);
        confirmed.CancelledAt.Should().Be(OccurredAt);
        payloads.Select(ReadRefund).Should().BeEquivalentTo([0L, 125_001L]);
        payloads.Should().OnlyContain(payload => PayloadIsCanonicalCancellation(payload));
        await fixture.History.Received(2).AddAsync(
            Arg.Is<BookingStatusHistory>(history =>
                history.Status == BookingStatus.CANCELLED
                && history.OccurredAt == OccurredAt
                && history.ActorUserId == OperatorId
                && history.ReasonCode == "OPERATOR_CANCELLED_TRIP"),
            Arg.Any<CancellationToken>());
        await fixture.UnitOfWork.Received(1).ExecuteInTransactionAsync(
            Arg.Any<Func<Task<int>>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicateDeliveryIsNoOpAfterRowsLeaveActiveStatuses()
    {
        var booking = CreateBooking(BookingStatus.CONFIRMED, 100_000);
        var fixture = new Fixture([booking]);

        (await fixture.Handler.Handle(Command(), CancellationToken.None)).Should().Be(1);
        fixture.Bookings.GetCancellableByTripAsync(TripId, OperatorId, Arg.Any<CancellationToken>()).Returns([]);
        (await fixture.Handler.Handle(Command(), CancellationToken.None)).Should().Be(0);

        await fixture.Outbox.Received(1).EnqueueAsync(
            "booking.booking.cancelled",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("WRONG_REASON")]
    [InlineData("")]
    public async Task RejectsInvalidCancellationReason(string reason)
    {
        var fixture = new Fixture([]);

        var act = () => fixture.Handler.Handle(Command() with { CancelReason = reason }, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        await fixture.UnitOfWork.DidNotReceiveWithAnyArgs().ExecuteInTransactionAsync<int>(default!, default);
    }

    [Fact]
    public async Task RejectsDifferentOccurredAndCancelledTimestamps()
    {
        var fixture = new Fixture([]);

        var act = () => fixture.Handler.Handle(
            Command() with { CancelledAt = OccurredAt.AddTicks(1) },
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static HandleTripCancelledCommand Command()
        => new(Guid.NewGuid(), OccurredAt, TripId, OperatorId, OccurredAt,
            HandleTripCancelledCommandHandler.DriverScheduleDayRemovedReason);

    private static long ReadRefund(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("refundAmount").GetInt64();
    }

    private static bool PayloadIsCanonicalCancellation(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        return root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal).SetEquals(
                ["bookingId", "bookingCode", "userId", "refundAmount", "refundOverride", "cancellationReason", "ticketCodes", "ticketCount"])
            && root.GetProperty("refundOverride").GetBoolean()
            && root.GetProperty("cancellationReason").GetString() == "OPERATOR_CANCELLED_TRIP";
    }

    private static BookingEntity CreateBooking(BookingStatus status, long totalAmount)
    {
        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(OccurredAt), Guid.NewGuid(), TripId, OperatorId, Guid.NewGuid(),
            null, null, null, Money.FromRaw(totalAmount), Money.Zero, Money.FromRaw(totalAmount));
        typeof(BookingEntity).GetProperty(nameof(BookingEntity.Status))!.SetValue(booking, status);
        return booking;
    }

    private sealed class Fixture
    {
        public Fixture(IReadOnlyList<BookingEntity> bookings)
        {
            Bookings.AcquireEventLockAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            Bookings.GetCancellableByTripAsync(TripId, OperatorId, Arg.Any<CancellationToken>()).Returns(bookings);
            UnitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<Task<int>>>(), Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<Func<Task<int>>>()());
            Handler = new HandleTripCancelledCommandHandler(Bookings, History, Outbox, UnitOfWork);
        }

        public IBookingRepository Bookings { get; } = Substitute.For<IBookingRepository>();
        public IBookingStatusHistoryRepository History { get; } = Substitute.For<IBookingStatusHistoryRepository>();
        public IIntegrationEventOutbox Outbox { get; } = Substitute.For<IIntegrationEventOutbox>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public HandleTripCancelledCommandHandler Handler { get; }
    }
}
