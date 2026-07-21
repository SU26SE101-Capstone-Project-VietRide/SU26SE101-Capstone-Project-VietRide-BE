using System.Security.Claims;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using VietRide.Booking.Api.Controllers;
using VietRide.Booking.Application.Features.Bookings.AcceptStopDisabledFallback;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using Xunit;

namespace VietRide.Booking.IntegrationTests.StopDisabled;

public sealed class Day24StopDisabledResolutionEndpointTests
{
    [Fact]
    public void BodylessFallback_UsesAutoFallbackResolution()
    {
        var action = BookingPendingAction.Create(Guid.NewGuid(), BookingPendingActionReason.STOP_DISABLED, DateTimeOffset.UtcNow);
        action.Resolve(BookingPendingActionResolved.AUTO_FALLBACK_DESTINATION, DateTimeOffset.UtcNow);
        action.ResolvedAction.Should().Be(BookingPendingActionResolved.AUTO_FALLBACK_DESTINATION);
    }

    [Fact]
    public async Task ControllerDispatchesBodylessFallbackWithRequiredIdempotencyKey()
    {
        var sender = Substitute.For<ISender>();
        var bookingId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var passengerId = Guid.NewGuid();
        sender.Send(Arg.Any<AcceptStopDisabledFallbackCommand>(), Arg.Any<CancellationToken>())
            .Returns(new AcceptStopDisabledFallbackResult(bookingId, actionId,
                nameof(BookingPendingActionResolved.AUTO_FALLBACK_DESTINATION), DateTimeOffset.UtcNow));
        var controller = new BookingsController(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([
                        new Claim(ClaimTypes.NameIdentifier, passengerId.ToString()),
                        new Claim(ClaimTypes.Role, "PASSENGER")], "test"))
                }
            }
        };
        controller.HttpContext.Request.Headers["Idempotency-Key"] = "fallback-key";

        var result = await controller.AcceptStopDisabledFallback(bookingId, actionId, default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(200);
        await sender.Received(1).Send(
            Arg.Is<AcceptStopDisabledFallbackCommand>(command =>
                command.BookingId == bookingId && command.ActionId == actionId
                && command.PassengerUserId == passengerId && command.IdempotencyKey == "fallback-key"),
            Arg.Any<CancellationToken>());
    }
}
