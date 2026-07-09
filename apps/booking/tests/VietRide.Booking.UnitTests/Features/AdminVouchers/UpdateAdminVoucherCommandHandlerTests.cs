using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.AdminVouchers.UpdateAdminVoucher;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.UnitTests.Features.AdminVouchers;

public sealed class UpdateAdminVoucherCommandHandlerTests
{
    private static readonly Guid AdminUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OperatorId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly IVoucherRepository _vouchers = Substitute.For<IVoucherRepository>();

    private UpdateAdminVoucherCommandHandler BuildSut() => new(
        _vouchers,
        NullLogger<UpdateAdminVoucherCommandHandler>.Instance);

    [Fact]
    public async Task Handle_PlatformVoucher_UpdatesApplicableServices()
    {
        var voucher = CreatePlatformVoucher();
        _vouchers.FindPlatformByIdAsync(voucher.Id, Arg.Any<CancellationToken>())
            .Returns(voucher);
        _vouchers.CountUsagesAsync(voucher.Id, Arg.Any<CancellationToken>())
            .Returns(0);

        var command = BuildCommand(voucher, applicableServices: ["PARCEL"]);

        var result = await BuildSut().Handle(command, CancellationToken.None);

        result.ApplicableServices.Should().Equal("PARCEL");
        voucher.ApplicableServices.Should().Equal("PARCEL");
        _vouchers.Received(1).Update(voucher);
    }

    [Fact]
    public async Task Handle_OperatorOwnedVoucher_ThrowsVoucherNotFound()
    {
        var voucherId = Guid.NewGuid();
        _vouchers.FindPlatformByIdAsync(voucherId, Arg.Any<CancellationToken>())
            .Returns((Voucher?)null);

        var command = BuildCommand(voucherId: voucherId);

        var act = () => BuildSut().Handle(command, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<CodedNotFoundException>();
        ex.Which.ErrorCode.Should().Be("VOUCHER_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_UsedVoucher_ChangingEconomicField_ThrowsVoucherLocked()
    {
        var voucher = CreatePlatformVoucher(value: 10_000);
        _vouchers.FindPlatformByIdAsync(voucher.Id, Arg.Any<CancellationToken>())
            .Returns(voucher);
        _vouchers.CountUsagesAsync(voucher.Id, Arg.Any<CancellationToken>())
            .Returns(1);

        var command = BuildCommand(voucher, value: 20_000);

        var act = () => BuildSut().Handle(command, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<CodedConflictException>();
        ex.Which.ErrorCode.Should().Be("VOUCHER_LOCKED");
    }

    private static Voucher CreatePlatformVoucher(long value = 10_000) =>
        Voucher.Create(
            code: "ADMIN001",
            name: "Admin Voucher",
            type: VoucherType.FIXED_AMOUNT,
            value: value,
            minOrderAmount: Money.FromRaw(50_000),
            maxDiscountAmount: null,
            totalUsageLimit: 100,
            perUserLimit: 1,
            validFrom: DateTimeOffset.UtcNow.AddDays(-1),
            validUntil: DateTimeOffset.UtcNow.AddDays(30),
            newUserOnly: false,
            applicablePaymentMethods: null,
            applicableServices: ["BOOKING"],
            applicableOperatorIds: null,
            applicableRouteIds: null,
            fundingType: VoucherFundingType.VIETRIDE_FUNDED,
            ownerOperatorId: null,
            createdByUserId: AdminUserId);

    private static UpdateAdminVoucherCommand BuildCommand(
        Voucher? voucher = null,
        Guid? voucherId = null,
        long? value = null,
        IReadOnlyList<string>? applicableServices = null) =>
        new(
            VoucherId: voucher?.Id ?? voucherId ?? Guid.NewGuid(),
            Name: voucher?.Name ?? "Admin Voucher",
            Value: value ?? voucher?.Value,
            MinOrderAmount: voucher?.MinOrderAmount.Amount,
            MaxDiscountAmount: voucher?.MaxDiscountAmount?.Amount,
            TotalUsageLimit: voucher?.TotalUsageLimit,
            PerUserLimit: voucher?.PerUserLimit,
            ValidFrom: voucher?.ValidFrom,
            ValidUntil: voucher?.ValidUntil,
            NewUserOnly: voucher?.NewUserOnly,
            ApplicablePaymentMethods: voucher?.ApplicablePaymentMethods,
            ApplicableServices: applicableServices ?? voucher?.ApplicableServices,
            ApplicableRouteIds: voucher?.ApplicableRouteIds);
}
