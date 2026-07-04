using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using VietRide.Booking.Api.Controllers;
using VietRide.Booking.Application.Features.Internal.Tracking;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.UnitTests.Features.Internal.Tracking;

public sealed class InternalTrackingControllerTests
{
    [Fact]
    public async Task GetTrackingAuthorizationAsync_SendsBookingAuthorizationQuery()
    {
        var tripId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetBookingTrackingAuthorizationQuery>(), Arg.Any<CancellationToken>())
            .Returns(new TrackingBookingAuthorizationResponse(true, "BOOKING_OWNER"));
        var controller = CreateController(mediator);

        var response = await controller.GetTrackingAuthorizationAsync(
            tripId,
            userId,
            "PASSENGER",
            CancellationToken.None);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<TrackingBookingAuthorizationResponse>>().Subject;
        envelope.Success.Should().BeTrue();
        envelope.Data.Should().BeEquivalentTo(new TrackingBookingAuthorizationResponse(true, "BOOKING_OWNER"));
        await mediator.Received(1).Send(
            Arg.Is<GetBookingTrackingAuthorizationQuery>(query =>
                query.TripId == tripId
                && query.UserId == userId
                && query.Role == "PASSENGER"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPickupBookingsAsync_SendsPickupBookingsQuery()
    {
        var tripId = Guid.NewGuid();
        var stopId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var passengerUserId = Guid.NewGuid();
        var responseBody = new PickupBookingsTrackingResponse(
        [
            new PickupBookingTrackingDto(bookingId, passengerUserId, stopId, "CONFIRMED", null),
        ]);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetPickupBookingsTrackingQuery>(), Arg.Any<CancellationToken>())
            .Returns(responseBody);
        var controller = CreateController(mediator);

        var response = await controller.GetPickupBookingsAsync(tripId, stopId, CancellationToken.None);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<PickupBookingsTrackingResponse>>().Subject;
        envelope.Success.Should().BeTrue();
        envelope.Data.Should().BeEquivalentTo(responseBody);
        await mediator.Received(1).Send(
            Arg.Is<GetPickupBookingsTrackingQuery>(query =>
                query.TripId == tripId
                && query.StopId == stopId),
            Arg.Any<CancellationToken>());
    }

    private static InternalTrackingController CreateController(IMediator mediator)
        => new(mediator)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
}
