using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using VietRide.Booking.Api.Controllers;
using VietRide.Booking.Api.Controllers.Requests;
using VietRide.Booking.Application.Features.Vouchers.ListVouchers;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.UnitTests.Features.Vouchers;

public sealed class AdminVouchersControllerTests
{
    [Fact]
    public async Task ListVouchers_SendsPlatformOnlyQuery()
    {
        var sender = Substitute.For<ISender>();
        var response = PagedResult<VoucherListItem>.Create([], 1, 20, 0);
        sender.Send(Arg.Any<ListVouchersQuery>(), Arg.Any<CancellationToken>())
            .Returns(response);
        var controller = CreateController(sender);

        var result = await controller.ListVouchers(
            new ListVouchersRequest { FundingType = "OPERATOR_FUNDED", IsActive = true },
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        await sender.Received(1).Send(
            Arg.Is<ListVouchersQuery>(query =>
                query.OwnerOperatorId == null
                && query.PlatformOnly
                && query.FundingType == "OPERATOR_FUNDED"
                && query.IsActive == true),
            Arg.Any<CancellationToken>());
    }

    private static AdminVouchersController CreateController(ISender sender)
        => new(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
}
