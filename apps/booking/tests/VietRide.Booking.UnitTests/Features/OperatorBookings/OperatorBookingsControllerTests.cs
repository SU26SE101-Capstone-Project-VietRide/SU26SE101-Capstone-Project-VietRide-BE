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
using VietRide.Booking.Application.Features.OperatorBookings.GetOperatorBookingDetail;
using VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.UnitTests.Features.OperatorBookings;

public sealed class OperatorBookingsControllerTests
{
    [Theory]
    [InlineData("operator_id")]
    [InlineData("operatorId")]
    public async Task List_SendsFrozenFiltersScopedToOperatorClaim(string claimType)
    {
        var operatorId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<ListOperatorBookingsQuery>(), Arg.Any<CancellationToken>())
            .Returns(PagedResult<OperatorBookingListItem>.Create([], 2, 50, 0));
        var controller = CreateController(sender, claimType, operatorId.ToString());
        var request = new ListOperatorBookingsRequest
        {
            Status = "CONFIRMED,CANCELLED",
            TripId = tripId,
            Date = new DateOnly(2026, 7, 11),
            PassengerPhone = "+84901234567",
            BookingCode = " BK-001 ",
            Page = 2,
            PageSize = 50,
            SortBy = "departureAt",
            SortDir = "asc",
        };

        var result = await controller.List(request, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        await sender.Received(1).Send(
            Arg.Is<ListOperatorBookingsQuery>(query =>
                query.OperatorId == operatorId
                && query.Status == request.Status
                && query.TripId == tripId
                && query.Date == request.Date
                && query.PassengerPhone == request.PassengerPhone
                && query.BookingCode == request.BookingCode
                && query.Page == 2
                && query.PageSize == 50
                && query.SortBy == "departureAt"
                && query.SortDir == "asc"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetById_SendsBookingAndOperatorIds()
    {
        var operatorId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var sender = Substitute.For<ISender>();
        var controller = CreateController(sender, "operatorId", operatorId.ToString());

        await controller.GetById(bookingId, CancellationToken.None);

        await sender.Received(1).Send(
            Arg.Is<GetOperatorBookingDetailQuery>(query =>
                query.BookingId == bookingId && query.OperatorId == operatorId),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("operatorId", "not-a-guid")]
    [InlineData("operator_id", "00000000-0000-0000-0000-000000000000")]
    public async Task Routes_MissingOrInvalidOperatorClaim_ReturnForbiddenWithoutSending(
        string? claimType,
        string? claimValue)
    {
        var sender = Substitute.For<ISender>();
        var controller = CreateController(sender, claimType, claimValue);

        var list = await controller.List(new ListOperatorBookingsRequest(), CancellationToken.None);
        var detail = await controller.GetById(Guid.NewGuid(), CancellationToken.None);

        list.Result.Should().BeOfType<ForbidResult>();
        detail.Result.Should().BeOfType<ForbidResult>();
        await sender.DidNotReceiveWithAnyArgs().Send(default!, default);
    }

    [Fact]
    public void Controller_AuthorizesExactlyFrozenRolesAndDocumentsBothRoutes()
    {
        var authorize = typeof(OperatorBookingsController).GetCustomAttribute<AuthorizeAttribute>();
        authorize.Should().NotBeNull();
        authorize!.Roles.Should().Be("OPERATOR_ADMIN,OPERATOR_STAFF");

        var actions = typeof(OperatorBookingsController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.DeclaringType == typeof(OperatorBookingsController))
            .ToArray();

        actions.Should().ContainSingle(method => method.Name == nameof(OperatorBookingsController.List));
        actions.Should().ContainSingle(method => method.Name == nameof(OperatorBookingsController.GetById));
        actions.Where(method => method.Name is nameof(OperatorBookingsController.List) or nameof(OperatorBookingsController.GetById))
            .Should().OnlyContain(method => method.GetCustomAttributes<ProducesResponseTypeAttribute>().Any());
    }

    private static OperatorBookingsController CreateController(
        ISender sender,
        string? operatorClaimType,
        string? operatorClaimValue)
    {
        var claims = new List<Claim> { new("sub", Guid.NewGuid().ToString()) };
        if (operatorClaimType is not null && operatorClaimValue is not null)
        {
            claims.Add(new Claim(operatorClaimType, operatorClaimValue));
        }

        return new OperatorBookingsController(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
                },
            },
        };
    }
}
