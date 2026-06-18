using FluentAssertions;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.UnitTests.Domain;

public class BookingTests
{
    private static readonly Guid PassengerUserId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid TripId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid OperatorId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
    private static readonly Guid PickupStationId = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
    private static readonly Guid PickupStopId = Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");
    private static readonly Guid DropoffStationId = Guid.Parse("ffffffff-ffff-4fff-8fff-ffffffffffff");
    private static readonly Guid DropoffStopId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public void ChangePickup_WhenStatusIsNotConfirmed_ThrowsInvalidOperationException()
    {
        var booking = CreateBooking();

        var act = () => booking.ChangePickup(PickupStopId, null);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ChangePickup_RequiresExactlyOnePickupTarget()
    {
        var booking = CreateConfirmedBooking();

        var noPickup = () => booking.ChangePickup(null, null);
        var twoPickups = () => booking.ChangePickup(PickupStationId, PickupStopId);

        noPickup.Should().Throw<ArgumentException>();
        twoPickups.Should().Throw<ArgumentException>();

        booking.ChangePickup(null, PickupStopId);

        booking.PickupStationId.Should().BeNull();
        booking.PickupStopId.Should().Be(PickupStopId);
    }

    [Fact]
    public void ChangeDropoff_WhenStatusIsNotConfirmed_ThrowsInvalidOperationException()
    {
        var booking = CreateBooking();

        var act = () => booking.ChangeDropoff(DropoffStationId, null);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ChangeDropoff_AllowsAtMostOneTargetAndClearsTheOtherColumn()
    {
        var booking = CreateConfirmedBooking();

        var twoDropoffs = () => booking.ChangeDropoff(DropoffStationId, DropoffStopId);

        twoDropoffs.Should().Throw<ArgumentException>();

        booking.ChangeDropoff(DropoffStationId, null);

        booking.DropoffStationId.Should().Be(DropoffStationId);
        booking.DropoffStopId.Should().BeNull();

        booking.ChangeDropoff(null, DropoffStopId);

        booking.DropoffStationId.Should().BeNull();
        booking.DropoffStopId.Should().Be(DropoffStopId);
    }

    [Fact]
    public void AssignRoundTripGroup_SetsBookingGroupIdAndTripDirection()
    {
        var booking = CreateBooking();
        var bookingGroupId = Guid.Parse("22222222-2222-4222-8222-222222222222");

        booking.AssignRoundTripGroup(bookingGroupId, TripDirection.RETURN);

        booking.BookingGroupId.Should().Be(bookingGroupId);
        booking.TripDirection.Should().Be(TripDirection.RETURN);
    }

    private static BookingEntity CreateConfirmedBooking()
    {
        var booking = CreateBooking();
        booking.Confirm(DateTimeOffset.UtcNow);
        return booking;
    }

    private static BookingEntity CreateBooking()
        => BookingEntity.CreatePendingPayment(
            bookingCode: BookingCode.Generate(DateTimeOffset.UtcNow),
            passengerUserId: PassengerUserId,
            tripId: TripId,
            operatorId: OperatorId,
            pickupStationId: PickupStationId,
            pickupStopId: null,
            dropoffStationId: null,
            dropoffStopId: null,
            baseFare: Money.FromRaw(200_000),
            discountAmount: Money.Zero,
            totalAmount: Money.FromRaw(200_000));
}
