using FluentAssertions;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Infrastructure.Jobs;

namespace VietRide.Booking.UnitTests.Jobs;

public sealed class Day24StopDisabledAutoFallbackJobTests
{
    [Fact]
    public void EventIdentity_IsDeterministicPerPendingAction()
    {
        var actionId = Guid.NewGuid();
        StopDisabledAutoFallbackJob.DeriveEventId(actionId)
            .Should().Be(StopDisabledAutoFallbackJob.DeriveEventId(actionId));
        StopDisabledAutoFallbackJob.DeriveEventId(actionId)
            .Should().NotBe(StopDisabledAutoFallbackJob.DeriveEventId(Guid.NewGuid()));
    }

    [Fact]
    public void Event_UsesRatifiedRoutingKeyAndFields()
    {
        var evt = new BookingStopDisabledAutoFallbackIntegrationEvent(
            Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "PICKUP", Guid.NewGuid());

        evt.EventType.Should().Be("booking.booking.stop_disabled_auto_fallback_applied");
        evt.ResolvedAction.Should().Be("AUTO_FALLBACK_DESTINATION");
        evt.AffectedField.Should().Be("PICKUP");
    }
}
