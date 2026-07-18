using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using VietRide.Booking.Api.Controllers;
using VietRide.Booking.Api.Controllers.Requests;
using VietRide.Booking.Application.Features.Bookings.ResolvePendingAction;
using VietRide.Shared.Web.Idempotency;

namespace VietRide.Booking.UnitTests.Controllers;

public sealed class Day23ResolveScheduleActionControllerTests
{
    [Fact]
    public async Task EndpointIsPassengerOnlyIdempotentThinMediatRDispatchWithExactSwaggerStatuses()
    {
        var sender = Substitute.For<ISender>();
        var bookingId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var passengerId = Guid.NewGuid();
        var key = Guid.NewGuid().ToString("D");
        var expected = new ResolvePendingActionResult(bookingId, actionId, "ACCEPTED", DateTimeOffset.UtcNow);
        sender.Send(Arg.Any<ResolvePendingActionCommand>(), Arg.Any<CancellationToken>()).Returns(expected);
        var controller = new BookingsController(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("sub", passengerId.ToString()), new Claim(ClaimTypes.Role, "PASSENGER")],
                        "test")),
                },
            },
        };
        controller.Request.Headers["Idempotency-Key"] = key;

        var response = await controller.ResolvePendingAction(
            bookingId.ToString(),
            actionId.ToString(),
            new ResolvePendingActionRequest { Action = "ACCEPTED", Note = "ok" },
            CancellationToken.None);

        response.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(200);
        await sender.Received(1).Send(
            Arg.Is<ResolvePendingActionCommand>(command =>
                command.BookingId == bookingId
                && command.ActionId == actionId
                && command.PassengerUserId == passengerId
                && command.IdempotencyKey == key
                && command.Action == "ACCEPTED"),
            Arg.Any<CancellationToken>());

        var method = typeof(BookingsController).GetMethod(nameof(BookingsController.ResolvePendingAction))!;
        method.GetCustomAttribute<RequireIdempotencyAttribute>().Should().NotBeNull();
        method.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("PASSENGER");
        method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .Should().BeEquivalentTo([200, 401, 403, 404, 409, 422]);
    }

    [Theory]
    [InlineData("not-a-booking-id", "11111111-1111-4111-8111-111111111111", "bookingId")]
    [InlineData("11111111-1111-4111-8111-111111111111", "not-an-action-id", "actionId")]
    public async Task MalformedRouteUuidThrowsValidationErrorBeforeDispatch(
        string bookingId,
        string actionId,
        string expectedField)
    {
        var sender = Substitute.For<ISender>();
        var controller = new BookingsController(sender)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var act = () => controller.ResolvePendingAction(
            bookingId,
            actionId,
            new ResolvePendingActionRequest { Action = "ACCEPTED" },
            CancellationToken.None);

        var exception = (await act.Should()
            .ThrowAsync<VietRide.Shared.Application.Exceptions.CodedValidationException>()).Which;
        exception.ErrorCode.Should().Be("VALIDATION_ERROR");
        exception.Errors.Should().ContainSingle(error => error.Field == expectedField);
        await sender.DidNotReceiveWithAnyArgs().Send(default!, default);
    }
}
