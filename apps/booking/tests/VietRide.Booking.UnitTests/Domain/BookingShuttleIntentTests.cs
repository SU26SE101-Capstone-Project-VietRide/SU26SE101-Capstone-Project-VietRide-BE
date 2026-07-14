using FluentAssertions;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.UnitTests.Domain;

public sealed class BookingShuttleIntentTests
{
    [Fact]
    public void RequestShuttle_ForStationPickup_CreatesSingleActiveIntent()
    {
        var booking = CreateBooking(Guid.NewGuid(), null);

        booking.RequestShuttle("123 Nguyen Hue", 10.77m, 106.70m);

        booking.ShuttleIntent.Should().NotBeNull();
        booking.ShuttleIntent!.IsActive.Should().BeTrue();
        FluentActions.Invoking(() => booking.RequestShuttle("Other", 10m, 106m))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RequestShuttle_ForStopPickup_Throws()
    {
        var booking = CreateBooking(null, Guid.NewGuid());

        FluentActions.Invoking(() => booking.RequestShuttle("123 Nguyen Hue", 10.77m, 106.70m))
            .Should().Throw<InvalidOperationException>();
    }

    private static BookingEntity CreateBooking(Guid? stationId, Guid? stopId)
        => BookingEntity.CreatePendingPayment(
            BookingCode.Parse("VR-20260713-ABCDEF12"),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            stationId,
            stopId,
            null,
            null,
            Money.FromRaw(100_000),
            Money.Zero,
            Money.FromRaw(100_000));
}
