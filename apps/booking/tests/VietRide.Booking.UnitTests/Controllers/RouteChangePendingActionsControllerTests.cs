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
using VietRide.Booking.Application.Features.PendingActions;
using VietRide.Shared.Web.Idempotency;

namespace VietRide.Booking.UnitTests.Controllers;

public sealed class RouteChangePendingActionsControllerTests
{
    [Fact]
    public async Task ThinMediatRAdrEnvelopeAuthTenantIdempotencyAndMetadata()
    {
        var sender = Substitute.For<ISender>();
        var bookingId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var passengerId = Guid.NewGuid();
        var candidateStopId = Guid.NewGuid();
        var key = Guid.NewGuid().ToString("D");
        sender.Send(Arg.Any<ResolveRouteChangePendingActionCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ResolvePendingActionResult(
                bookingId,
                actionId,
                "ACCEPTED",
                DateTimeOffset.UtcNow));
        var controller = new PendingActionsController(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [
                            new Claim("sub", passengerId.ToString()),
                            new Claim(ClaimTypes.Role, "PASSENGER"),
                        ],
                        "test")),
                },
            },
        };
        controller.Request.Headers["Idempotency-Key"] = key;

        var response = await controller.Resolve(
            bookingId.ToString(),
            actionId.ToString(),
            new ResolvePendingActionRequest
            {
                Action = "ACCEPTED",
                SelectedStopId = candidateStopId,
                Note = "near gate",
            },
            CancellationToken.None);

        response.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(200);
        await sender.Received(1).Send(
            Arg.Is<ResolveRouteChangePendingActionCommand>(command =>
                command.BookingId == bookingId
                && command.ActionId == actionId
                && command.PassengerUserId == passengerId
                && command.IdempotencyKey == key
                && command.SelectedStopId == candidateStopId
                && command.SelectedStationId == null),
            Arg.Any<CancellationToken>());
        var method = typeof(PendingActionsController).GetMethod(nameof(PendingActionsController.Resolve))!;
        method.GetCustomAttribute<RequireIdempotencyAttribute>().Should().NotBeNull();
        method.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("PASSENGER");
        method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .Should().BeEquivalentTo([200, 401, 403, 404, 409, 422]);
    }
}
