using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using VietRide.Booking.Api.Controllers;
using VietRide.Booking.Application.Features.Bookings.GetBookingStatus;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.UnitTests.Features.Bookings;

public sealed class BookingsControllerTests
{
    [Fact]
    public async Task GetBookingStatus_SendsBookingAndAuthenticatedPassengerIds()
    {
        var passengerUserId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetBookingStatusQuery>(), Arg.Any<CancellationToken>())
            .Returns(new GetBookingStatusResult(bookingId, "CONFIRMED"));
        var controller = CreatePassengerController(sender, passengerUserId);

        var result = await controller.GetBookingStatus(bookingId, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        await sender.Received(1).Send(
            Arg.Is<GetBookingStatusQuery>(query =>
                query.BookingId == bookingId && query.PassengerUserId == passengerUserId && query.OperatorId == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetBookingStatus_AuthorizedOperator_SendsOperatorId()
    {
        var operatorId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetBookingStatusQuery>(), Arg.Any<CancellationToken>())
            .Returns(new GetBookingStatusResult(bookingId, "CONFIRMED"));
        var controller = CreateOperatorController(sender, operatorId);

        var result = await controller.GetBookingStatus(bookingId, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        await sender.Received(1).Send(
            Arg.Is<GetBookingStatusQuery>(query =>
                query.BookingId == bookingId && query.PassengerUserId == null && query.OperatorId == operatorId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void GetBookingStatus_RequiresAuthorizedRoleAndDocumentsEnvelopeResponses()
    {
        var action = typeof(BookingsController).GetMethod(nameof(BookingsController.GetBookingStatus));
        action.Should().NotBeNull();
        var endpoint = action!;
        endpoint.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("PASSENGER,OPERATOR_ADMIN,OPERATOR_STAFF");
        endpoint.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .Should().BeEquivalentTo([StatusCodes.Status200OK, StatusCodes.Status401Unauthorized, StatusCodes.Status403Forbidden, StatusCodes.Status404NotFound]);
        endpoint.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Single(attribute => attribute.StatusCode == StatusCodes.Status200OK).Type
            .Should().Be(typeof(ApiResponse<GetBookingStatusResult>));
    }

    [Theory]
    [InlineData(nameof(BookingsController.CreateBooking))]
    [InlineData(nameof(BookingsController.CreateRoundTripBooking))]
    public void CreateEndpoints_DocumentUpstreamUnavailable(string methodName)
    {
        var endpoint = typeof(BookingsController).GetMethod(methodName)!;

        endpoint.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .Should().Contain(StatusCodes.Status502BadGateway);
    }

    private static BookingsController CreatePassengerController(ISender sender, Guid passengerUserId)
        => new(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", passengerUserId.ToString()), new Claim(ClaimTypes.Role, "PASSENGER")], "test")),
                },
            },
        };

    private static BookingsController CreateOperatorController(ISender sender, Guid operatorId)
        => new(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("operator_id", operatorId.ToString()), new Claim(ClaimTypes.Role, "OPERATOR_ADMIN")],
                        "test")),
                },
            },
        };
}
