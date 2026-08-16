using FluentAssertions;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Domain;

public sealed class TripSeatTests
{
    [Fact]
    public void DisableAndEnable_TransitionsAndClearsReason()
    {
        var seat = TripSeat.Create(Guid.NewGuid(), " a1 ");

        seat.Disable("Ghế hỏng nội thất");
        seat.Status.Should().Be(TripSeatStatus.UNAVAILABLE);
        seat.DisabledReason.Should().Be("Ghế hỏng nội thất");

        seat.Enable();
        seat.Status.Should().Be(TripSeatStatus.AVAILABLE);
        seat.DisabledReason.Should().BeNull();
    }

    [Fact]
    public void Disable_RejectsBlankReason()
    {
        var seat = TripSeat.Create(Guid.NewGuid(), "A1");

        FluentActions.Invoking(() => seat.Disable("  "))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Disable_RejectsHeldAndBookedSeats()
    {
        var held = TripSeat.Create(Guid.NewGuid(), "A1");
        held.MarkHeld();
        FluentActions.Invoking(() => held.Disable("broken"))
            .Should().Throw<InvalidOperationException>();

        var booked = TripSeat.Create(Guid.NewGuid(), "A2");
        booked.MarkHeld();
        booked.MarkBooked(Guid.NewGuid());
        FluentActions.Invoking(() => booked.Disable("broken"))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Enable_RejectsAvailableSeat()
    {
        var seat = TripSeat.Create(Guid.NewGuid(), "A1");

        FluentActions.Invoking(seat.Enable)
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Create_NormalizesSeatNumber()
    {
        TripSeat.Create(Guid.NewGuid(), " a1 ").SeatNumber.Should().Be("A1");
    }

    [Fact]
    public void MarkBooked_RejectsEmptyBookingId()
    {
        var seat = TripSeat.Create(Guid.NewGuid(), "A1");
        seat.MarkHeld();

        FluentActions.Invoking(() => seat.MarkBooked(Guid.Empty))
            .Should().Throw<ArgumentException>();
        seat.Status.Should().Be(TripSeatStatus.HELD);
        seat.BookingId.Should().BeNull();
    }

    [Fact]
    public void ReleaseBooked_RequiresOwnerAndClearsOwnership()
    {
        var bookingId = Guid.NewGuid();
        var seat = TripSeat.Create(Guid.NewGuid(), "A1");
        seat.MarkHeld();
        seat.MarkBooked(bookingId);

        FluentActions.Invoking(() => seat.ReleaseBooked(Guid.NewGuid()))
            .Should().Throw<InvalidOperationException>();
        seat.Status.Should().Be(TripSeatStatus.BOOKED);
        seat.BookingId.Should().Be(bookingId);

        seat.ReleaseBooked(bookingId);

        seat.Status.Should().Be(TripSeatStatus.AVAILABLE);
        seat.BookingId.Should().BeNull();

        seat.Hold();
        seat.Status.Should().Be(TripSeatStatus.HELD);
    }
}
