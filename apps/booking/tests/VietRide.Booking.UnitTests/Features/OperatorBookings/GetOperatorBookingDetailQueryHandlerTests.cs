using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.OperatorBookings.GetOperatorBookingDetail;
using VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.UnitTests.Features.OperatorBookings;

public sealed class GetOperatorBookingDetailQueryHandlerTests
{
    private readonly IBookingRepository _repository = Substitute.For<IBookingRepository>();
    private readonly Guid _bookingId = Guid.NewGuid();
    private readonly Guid _operatorId = Guid.NewGuid();

    [Fact]
    public async Task Handle_OwnTenant_ReturnsExactLeanProjectionAndDoesNotProbeExistence()
    {
        var detail = Detail();
        _repository.GetOperatorBookingDetailAsync(_bookingId, _operatorId, Arg.Any<CancellationToken>()).Returns(detail);

        var result = await new GetOperatorBookingDetailQueryHandler(_repository)
            .Handle(new(_bookingId, _operatorId), default);

        result.Should().BeSameAs(detail);
        typeof(OperatorBookingDetailDto).GetProperties().Select(property => property.Name).Should().BeEquivalentTo(
            ["Id", "BookingCode", "BuyerUserId", "TripId", "Status", "Trip", "SeatCount", "BaseFare",
             "DiscountAmount", "TotalAmount", "PickupStationId", "PickupStopId", "DropoffStationId",
             "DropoffStopId", "BookingGroupId", "TripDirection", "CancellationReason", "CreatedAt", "Seats", "StatusTimeline"]);
        typeof(OperatorBookingStatusTimelineDto).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo(["Status", "OccurredAt", "ReasonCode"]);
        await _repository.DidNotReceive().BookingExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForeignBooking_ThrowsForbiddenAfterExistenceOnlyProbe()
    {
        _repository.GetOperatorBookingDetailAsync(_bookingId, _operatorId, Arg.Any<CancellationToken>())
            .Returns((OperatorBookingDetailDto?)null);
        _repository.BookingExistsAsync(_bookingId, Arg.Any<CancellationToken>()).Returns(true);

        var action = () => new GetOperatorBookingDetailQueryHandler(_repository).Handle(new(_bookingId, _operatorId), default);

        (await action.Should().ThrowAsync<ForbiddenException>()).Which.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task Handle_UnknownBooking_ThrowsNotFound()
    {
        _repository.GetOperatorBookingDetailAsync(_bookingId, _operatorId, Arg.Any<CancellationToken>())
            .Returns((OperatorBookingDetailDto?)null);
        _repository.BookingExistsAsync(_bookingId, Arg.Any<CancellationToken>()).Returns(false);

        var action = () => new GetOperatorBookingDetailQueryHandler(_repository).Handle(new(_bookingId, _operatorId), default);

        (await action.Should().ThrowAsync<CodedNotFoundException>()).Which.ErrorCode.Should().Be("BOOKING_NOT_FOUND");
    }

    private OperatorBookingDetailDto Detail()
        => new(_bookingId, "VR-CODE", Guid.NewGuid(), Guid.NewGuid(), "CANCELLED",
            new OperatorBookingTripDto(
                "Route", "Origin", "Destination", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), 1,
            100_000, 0, 100_000, Guid.NewGuid(), null, Guid.NewGuid(), null, null, null, "USER_INITIATED",
            DateTimeOffset.UtcNow,
            [new(Guid.NewGuid(), Guid.NewGuid(), "VT-CODE", "A1", "CANCELLED", "PENDING")],
            [new("PENDING_PAYMENT", DateTimeOffset.UtcNow, null), new("CANCELLED", DateTimeOffset.UtcNow, "USER_INITIATED")]);
}
