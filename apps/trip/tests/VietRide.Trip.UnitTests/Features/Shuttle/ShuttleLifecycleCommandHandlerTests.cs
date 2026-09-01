using FluentAssertions;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.Shuttle;
using VietRide.Trip.UnitTests.Features.Vehicles;

namespace VietRide.Trip.UnitTests.Features.Shuttle;

public sealed class ShuttleLifecycleCommandHandlerTests
{
    [Fact]
    public async Task CancelTrip_ForwardsAuthenticatedActorToDispatchService()
    {
        var command = new CancelShuttleTripCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Vehicle unavailable");
        var expected = new ShuttleLifecycleResult(
            command.ShuttleTripId,
            "CANCELLED",
            2,
            DateTimeOffset.UtcNow);
        var service = TestProxy<IShuttleDispatchService>.Create((method, args) =>
        {
            if (method.Name != nameof(IShuttleDispatchService.CancelShuttleTripAsync))
            {
                return null;
            }

            Assert.Equal(command.OperatorId, args![0]);
            Assert.Equal(command.ShuttleTripId, args[1]);
            Assert.Equal(command.ActorUserId, args[2]);
            Assert.Equal(command.Reason, args[3]);
            return expected;
        });
        var handler = new CancelShuttleTripCommandHandler(service);

        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().Be(expected);
    }

    [Fact]
    public async Task UnassignBooking_ForwardsTenantActorAndReasonToDispatchService()
    {
        var command = new UnassignShuttleBookingCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Assigned by mistake");
        var expected = new UnassignShuttleBookingResult(
            command.ShuttleTripId,
            command.BookingId,
            2,
            1,
            "SCHEDULED",
            true,
            false,
            DateTimeOffset.UtcNow);
        var service = TestProxy<IShuttleDispatchService>.Create((method, args) =>
        {
            if (method.Name != nameof(IShuttleDispatchService.UnassignBookingAsync))
            {
                return null;
            }

            Assert.Equal(command.OperatorId, args![0]);
            Assert.Equal(command.ShuttleTripId, args[1]);
            Assert.Equal(command.BookingId, args[2]);
            Assert.Equal(command.ActorUserId, args[3]);
            Assert.Equal(command.Reason, args[4]);
            return expected;
        });
        var handler = new UnassignShuttleBookingCommandHandler(service);

        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().Be(expected);
    }
}
