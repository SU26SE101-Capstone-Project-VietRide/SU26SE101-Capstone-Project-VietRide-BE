using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Application.Features.Bookings.AcceptStopDisabledFallback;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using Xunit;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.UnitTests.Features.Bookings;

public sealed class Day24StopDisabledPassengerChoiceTests
{
    [Fact]
    public void DeadlineEquality_RemainsEligible()
    {
        var deadline = DateTimeOffset.UtcNow;
        var action = BookingPendingAction.Create(Guid.NewGuid(), BookingPendingActionReason.STOP_DISABLED, deadline);
        action.IsDeadlineExpired(deadline).Should().BeFalse();
    }

    [Fact]
    public void FallbackResolution_IsTerminalAndDeterministic()
    {
        var action = BookingPendingAction.Create(Guid.NewGuid(), BookingPendingActionReason.STOP_DISABLED, DateTimeOffset.UtcNow);
        action.Resolve(BookingPendingActionResolved.AUTO_FALLBACK_DESTINATION, DateTimeOffset.UtcNow);
        action.ResolvedAction.Should().Be(BookingPendingActionResolved.AUTO_FALLBACK_DESTINATION);
    }

    [Fact]
    public async Task FallbackHandler_ChangesPickupAndResolvesAtDeadlineEquality()
    {
        var now = DateTimeOffset.UtcNow;
        var passengerId = Guid.NewGuid();
        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(now), passengerId, Guid.NewGuid(), Guid.NewGuid(),
            null, Guid.NewGuid(), null, null, Money.FromRaw(100_000), Money.Zero, Money.FromRaw(100_000),
            tripSnapshotDeparture: now.AddHours(6));
        booking.Confirm(now.AddMinutes(-1));
        var fallbackStationId = Guid.NewGuid();
        var action = BookingPendingAction.Create(
            booking.Id, BookingPendingActionReason.STOP_DISABLED, now, null,
            JsonSerializer.Serialize(new { affectedField = "PICKUP", fallbackStationId }));
        var pendingActions = Substitute.For<IBookingPendingActionRepository>();
        pendingActions.GetByIdForUpdateAsync(action.Id, Arg.Any<CancellationToken>()).Returns(action);
        var bookings = Substitute.For<IBookingRepository>();
        bookings.FindByIdForUpdateAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var stationCanonicalizer = Substitute.For<IBookingStationCanonicalizer>();
        stationCanonicalizer.LockAndResolveAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var stationIds = call.Arg<IReadOnlyCollection<Guid>>();
                return new StationCanonicalizationResult(
                    stationIds.ToDictionary(id => id),
                    stationIds.ToHashSet());
            });

        var result = await new AcceptStopDisabledFallbackCommandHandler(
                pendingActions,
                bookings,
                stationCanonicalizer,
                clock)
            .Handle(new AcceptStopDisabledFallbackCommand(booking.Id, action.Id, passengerId, "key"), default);

        result.ResolvedAction.Should().Be(nameof(BookingPendingActionResolved.AUTO_FALLBACK_DESTINATION));
        booking.PickupStationId.Should().Be(fallbackStationId);
        action.ResolvedAction.Should().Be(BookingPendingActionResolved.AUTO_FALLBACK_DESTINATION);
        await stationCanonicalizer.Received(1).LockAndResolveAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { fallbackStationId })),
            Arg.Any<CancellationToken>());
        bookings.Received(1).Update(booking);
        pendingActions.Received(1).Update(action);
    }
}
