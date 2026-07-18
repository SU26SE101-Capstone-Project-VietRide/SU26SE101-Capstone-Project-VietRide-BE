using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.Bookings.HandleStopDisabled;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.UnitTests.Features.Bookings;

public sealed class HandleStopDisabledCommandHandlerTests
{
    [Fact]
    public async Task DeadlineUsesCurrentDepartureProjection()
    {
        var now = DateTimeOffset.Parse("2026-07-15T01:00:00Z");
        var operatorId = Guid.NewGuid();
        var stopId = Guid.NewGuid();
        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(now),
            Guid.NewGuid(),
            Guid.NewGuid(),
            operatorId,
            null,
            stopId,
            null,
            null,
            Money.FromRaw(100_000),
            Money.Zero,
            Money.FromRaw(100_000),
            tripSnapshotDeparture: now.AddDays(10));
        typeof(BookingEntity).GetProperty(nameof(BookingEntity.TripCurrentDeparture))!
            .SetValue(booking, now.AddHours(3));
        booking.Confirm(now.AddMinutes(-1));
        var bookings = Substitute.For<IBookingRepository>();
        bookings.QueryNoTracking().Returns(new[] { booking }.AsQueryable());
        var pendingActions = Substitute.For<IBookingPendingActionRepository>();
        pendingActions.Query().Returns(Array.Empty<BookingPendingAction>().AsQueryable());
        BookingPendingAction? captured = null;
        pendingActions.AddAsync(
                Arg.Do<BookingPendingAction>(action => captured = action),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<BookingPendingAction>());
        var stats = Substitute.For<IBookingStatsRepository>();
        stats.TryClaimProcessedEventAsync(
                "trip.stop.disabled",
                Arg.Any<Guid>(),
                now,
                Arg.Any<CancellationToken>())
            .Returns(true);
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        outbox.EnqueueAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var handler = new HandleStopDisabledCommandHandler(
            bookings, pendingActions, stats, outbox, unitOfWork, clock);

        (await handler.Handle(
            new HandleStopDisabledCommand(Guid.NewGuid(), stopId, operatorId, null),
            CancellationToken.None)).Should().Be(1);

        captured.Should().NotBeNull();
        captured!.Deadline.Should().Be(now.AddHours(1));
    }
}
