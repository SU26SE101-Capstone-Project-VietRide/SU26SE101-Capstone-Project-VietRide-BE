using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.Bookings.GetBookingStatus;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.UnitTests.Features.Bookings.GetBookingStatus;

public sealed class GetBookingStatusQueryHandlerTests
{
    private readonly IBookingRepository _bookings = Substitute.For<IBookingRepository>();
    private readonly Guid _bookingId = Guid.NewGuid();
    private readonly Guid _passengerUserId = Guid.NewGuid();

    [Fact]
    public async Task Handle_Owner_ReturnsExactlyBookingIdAndStatus()
    {
        var booking = BookingFor(_passengerUserId);
        _bookings.FindByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var result = await new GetBookingStatusQueryHandler(_bookings)
            .Handle(new GetBookingStatusQuery(booking.Id, _passengerUserId, null), CancellationToken.None);

        result.BookingId.Should().Be(booking.Id);
        result.Status.Should().Be(BookingStatus.PENDING_PAYMENT.ToString());
        typeof(GetBookingStatusResult).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo(["BookingId", "Status"]);
    }

    [Fact]
    public async Task Handle_UnknownBooking_ThrowsCanonicalNotFound()
    {
        _bookings.FindByIdAsync(_bookingId, Arg.Any<CancellationToken>()).Returns((BookingEntity?)null);

        var action = () => new GetBookingStatusQueryHandler(_bookings)
            .Handle(new GetBookingStatusQuery(_bookingId, _passengerUserId, null), CancellationToken.None);

        (await action.Should().ThrowAsync<CodedNotFoundException>()).Which.ErrorCode.Should().Be("BOOKING_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_NonOwner_ThrowsCanonicalNotFoundWithoutExistenceProbe()
    {
        _bookings.FindByIdAsync(_bookingId, Arg.Any<CancellationToken>()).Returns(BookingFor(Guid.NewGuid()));

        var action = () => new GetBookingStatusQueryHandler(_bookings)
            .Handle(new GetBookingStatusQuery(_bookingId, _passengerUserId, null), CancellationToken.None);

        (await action.Should().ThrowAsync<CodedNotFoundException>()).Which.ErrorCode.Should().Be("BOOKING_NOT_FOUND");
        await _bookings.DidNotReceive().BookingExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AuthorizedOperator_ReturnsStatus()
    {
        var operatorId = Guid.NewGuid();
        var booking = BookingFor(_passengerUserId, operatorId);
        _bookings.FindByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var result = await new GetBookingStatusQueryHandler(_bookings)
            .Handle(new GetBookingStatusQuery(booking.Id, null, operatorId), CancellationToken.None);

        result.BookingId.Should().Be(booking.Id);
    }

    [Fact]
    public async Task Handle_OtherOperator_ThrowsForbidden()
    {
        _bookings.FindByIdAsync(_bookingId, Arg.Any<CancellationToken>()).Returns(BookingFor(_passengerUserId));

        var action = () => new GetBookingStatusQueryHandler(_bookings)
            .Handle(new GetBookingStatusQuery(_bookingId, null, Guid.NewGuid()), CancellationToken.None);

        (await action.Should().ThrowAsync<ForbiddenException>()).Which.ErrorCode.Should().Be("FORBIDDEN");
    }

    private BookingEntity BookingFor(Guid passengerUserId, Guid? operatorId = null)
        => BookingEntity.CreatePendingPayment(
            BookingCode.Parse("VR-20260713-ABCDEFGH"),
            passengerUserId,
            Guid.NewGuid(),
            operatorId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            null,
            Money.FromRaw(100_000),
            Money.Zero,
            Money.FromRaw(100_000));
}
