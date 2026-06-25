using FluentAssertions;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.UnitTests.Domain;

public class BookingAggregateTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid StationId = Guid.NewGuid();

    private static BookingEntity CreateBooking(
        Guid? pickupStationId = null,
        Guid? pickupStopId = null)
    {
        var code = BookingCode.Generate(DateTimeOffset.UtcNow);
        var baseFare = Money.FromRaw(200_000);
        var total = Money.FromRaw(200_000);
        return BookingEntity.CreatePendingPayment(
            bookingCode: code,
            passengerUserId: UserId,
            tripId: TripId,
            operatorId: OperatorId,
            pickupStationId: pickupStationId ?? StationId,
            pickupStopId: pickupStopId,
            dropoffStationId: null,
            dropoffStopId: null,
            baseFare: baseFare,
            discountAmount: Money.Zero,
            totalAmount: total);
    }

    // --- Happy path tests ---

    [Fact]
    public void CreatePendingPayment_WithValidData_SetsStatusAndFields()
    {
        var booking = CreateBooking();

        booking.Status.Should().Be(BookingStatus.PENDING_PAYMENT);
        booking.PassengerUserId.Should().Be(UserId);
        booking.TripId.Should().Be(TripId);
        booking.PickupStationId.Should().Be(StationId);
        booking.TotalAmount.Amount.Should().Be(200_000);
    }

    [Fact]
    public void AddPassenger_UpToFive_Succeeds()
    {
        var booking = CreateBooking();

        for (var i = 1; i <= 5; i++)
        {
            booking.AddPassenger($"A{i}");
        }

        booking.Passengers.Should().HaveCount(5);
    }

    [Fact]
    public void Confirm_FromPendingPayment_SetsConfirmedStatus()
    {
        var booking = CreateBooking();
        var now = DateTimeOffset.UtcNow;

        booking.Confirm(now);

        booking.Status.Should().Be(BookingStatus.CONFIRMED);
        booking.ConfirmedAt.Should().Be(now);
    }

    [Fact]
    public void ExpirePayment_FromPendingPayment_SetsExpiredStatus()
    {
        var booking = CreateBooking();
        var now = DateTimeOffset.UtcNow;

        booking.ExpirePayment(now);

        booking.Status.Should().Be(BookingStatus.EXPIRED);
        booking.ExpiredAt.Should().Be(now);
    }

    [Fact]
    public void MarkRefunded_FromCancelled_SetsRefundedStatus()
    {
        var booking = CreateBooking();
        var cancelledAt = DateTimeOffset.UtcNow;
        var refundedAt = cancelledAt.AddMinutes(1);
        booking.Cancel(BookingCancellationReason.USER_INITIATED, cancelledAt);

        booking.MarkRefunded(refundedAt);

        booking.Status.Should().Be(BookingStatus.REFUNDED);
        booking.RefundedAt.Should().Be(refundedAt);
    }

    // --- Error / guard tests ---

    [Fact]
    public void CreatePendingPayment_BothPickupIds_Throws()
    {
        var code = BookingCode.Generate(DateTimeOffset.UtcNow);
        var act = () => BookingEntity.CreatePendingPayment(
            bookingCode: code,
            passengerUserId: UserId,
            tripId: TripId,
            operatorId: OperatorId,
            pickupStationId: StationId,
            pickupStopId: Guid.NewGuid(), // both set → violation
            dropoffStationId: null,
            dropoffStopId: null,
            baseFare: Money.FromRaw(100_000),
            discountAmount: Money.Zero,
            totalAmount: Money.FromRaw(100_000));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreatePendingPayment_TotalExceedsBase_Throws()
    {
        var code = BookingCode.Generate(DateTimeOffset.UtcNow);
        var act = () => BookingEntity.CreatePendingPayment(
            bookingCode: code,
            passengerUserId: UserId,
            tripId: TripId,
            operatorId: OperatorId,
            pickupStationId: StationId,
            pickupStopId: null,
            dropoffStationId: null,
            dropoffStopId: null,
            baseFare: Money.FromRaw(100_000),
            discountAmount: Money.Zero,
            totalAmount: Money.FromRaw(200_000)); // total > base

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddPassenger_SixthSeat_Throws()
    {
        var booking = CreateBooking();
        for (var i = 1; i <= 5; i++) booking.AddPassenger($"A{i}");

        var act = () => booking.AddPassenger("A6");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddPassenger_DuplicateSeat_Throws()
    {
        var booking = CreateBooking();
        booking.AddPassenger("A1");

        var act = () => booking.AddPassenger("A1");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Confirm_WhenAlreadyConfirmed_Throws()
    {
        var booking = CreateBooking();
        booking.Confirm(DateTimeOffset.UtcNow);

        var act = () => booking.Confirm(DateTimeOffset.UtcNow);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ExpirePayment_WhenConfirmed_Throws()
    {
        var booking = CreateBooking();
        booking.Confirm(DateTimeOffset.UtcNow);

        var act = () => booking.ExpirePayment(DateTimeOffset.UtcNow);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkRefunded_WhenPendingPayment_Throws()
    {
        var booking = CreateBooking();

        var act = () => booking.MarkRefunded(DateTimeOffset.UtcNow);

        act.Should().Throw<InvalidOperationException>();
    }
}
