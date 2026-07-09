using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using VietRide.Booking.Api.Controllers;
using VietRide.Booking.Api.Controllers.Requests;
using VietRide.Booking.Application.Features.AdminVouchers.DeleteAdminVoucher;
using VietRide.Booking.Application.Features.AdminVouchers.UpdateAdminVoucher;
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

    [Fact]
    public async Task UpdateVoucher_SendsUpdateAdminVoucherCommand()
    {
        var sender = Substitute.For<ISender>();
        var voucherId = Guid.NewGuid();
        var response = new UpdateAdminVoucherResult(
            voucherId,
            "PROMO1",
            "Updated",
            "FIXED_AMOUNT",
            20_000,
            "VIETRIDE_FUNDED",
            null,
            true,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30),
            false,
            [],
            ["PARCEL"],
            []);

        sender.Send(Arg.Any<UpdateAdminVoucherCommand>(), Arg.Any<CancellationToken>())
            .Returns(response);
        var controller = CreateController(sender);
        controller.Request.Headers["Idempotency-Key"] = "admin-update-voucher";

        var result = await controller.UpdateVoucher(
            voucherId,
            new UpdateAdminVoucherRequest { Name = "Updated", ApplicableServices = ["PARCEL"] },
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        await sender.Received(1).Send(
            Arg.Is<UpdateAdminVoucherCommand>(command =>
                command.VoucherId == voucherId
                && command.Name == "Updated"
                && command.ApplicableServices != null
                && command.ApplicableServices.SequenceEqual(new[] { "PARCEL" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteVoucher_SendsDeleteAdminVoucherCommand()
    {
        var sender = Substitute.For<ISender>();
        var voucherId = Guid.NewGuid();
        sender.Send(Arg.Any<DeleteAdminVoucherCommand>(), Arg.Any<CancellationToken>())
            .Returns(new DeleteAdminVoucherResult(voucherId, DateTimeOffset.UtcNow));
        var controller = CreateController(sender);
        controller.Request.Headers["Idempotency-Key"] = "admin-delete-voucher";

        var result = await controller.DeleteVoucher(voucherId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        await sender.Received(1).Send(
            Arg.Is<DeleteAdminVoucherCommand>(command => command.VoucherId == voucherId),
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
