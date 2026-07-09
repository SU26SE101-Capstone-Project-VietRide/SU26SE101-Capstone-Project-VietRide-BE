using System.Security.Claims;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using VietRide.Booking.Api.Controllers;
using VietRide.Booking.Api.Controllers.Requests;
using VietRide.Booking.Application.Features.Vouchers.ListVouchers;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.UnitTests.Features.OperatorVouchers;

public sealed class OperatorVouchersControllerTests
{
    [Fact]
    public async Task ListVouchers_SendsQueryScopedToCallerOperator()
    {
        var userId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var sender = Substitute.For<ISender>();
        var response = PagedResult<VoucherListItem>.Create([], 1, 20, 0);
        sender.Send(Arg.Any<ListVouchersQuery>(), Arg.Any<CancellationToken>())
            .Returns(response);
        var controller = CreateController(sender, userId, operatorId);

        var result = await controller.ListVouchers(
            new ListOperatorVouchersRequest { IsActive = true, SortBy = "createdAt", SortDir = "desc" },
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        await sender.Received(1).Send(
            Arg.Is<ListVouchersQuery>(query =>
                query.OwnerOperatorId == operatorId
                && query.PlatformOnly == false
                && query.FundingType == null
                && query.IsActive == true
                && query.Options.SortBy == "createdAt"
                && query.Options.SortDir == "desc"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListVouchers_MissingOperatorIdClaim_ThrowsUnauthorized()
    {
        var sender = Substitute.For<ISender>();
        var controller = CreateController(sender, Guid.NewGuid(), operatorId: null);

        var act = async () => await controller.ListVouchers(new ListOperatorVouchersRequest(), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Authenticated caller operatorId claim is missing or invalid.");
        await sender.DidNotReceiveWithAnyArgs().Send(Arg.Any<ListVouchersQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListVouchers_InvalidSortDir_ThrowsValidationBeforeSending()
    {
        var sender = Substitute.For<ISender>();
        var controller = CreateController(sender, Guid.NewGuid(), Guid.NewGuid());

        var act = async () => await controller.ListVouchers(
            new ListOperatorVouchersRequest { SortDir = "sideways" },
            CancellationToken.None);

        await act.Should().ThrowAsync<VietRide.Shared.Application.Exceptions.CodedValidationException>()
            .Where(ex => ex.ErrorCode == "INVALID_SORT_DIRECTION");
        await sender.DidNotReceiveWithAnyArgs().Send(Arg.Any<ListVouchersQuery>(), Arg.Any<CancellationToken>());
    }

    private static OperatorVouchersController CreateController(ISender sender, Guid userId, Guid? operatorId)
    {
        var claims = new List<Claim> { new("sub", userId.ToString()) };
        if (operatorId.HasValue)
            claims.Add(new Claim("operatorId", operatorId.Value.ToString()));

        return new OperatorVouchersController(sender)
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
