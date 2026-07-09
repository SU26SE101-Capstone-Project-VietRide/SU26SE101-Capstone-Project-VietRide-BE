using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.AdminVouchers.DeleteAdminVoucher;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.UnitTests.Features.AdminVouchers;

public sealed class DeleteAdminVoucherCommandHandlerTests
{
    private static readonly Guid AdminUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly IVoucherRepository _vouchers = Substitute.For<IVoucherRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private DeleteAdminVoucherCommandHandler BuildSut() => new(
        _vouchers,
        _clock,
        NullLogger<DeleteAdminVoucherCommandHandler>.Instance);

    [Fact]
    public async Task Handle_ActivePlatformVoucher_SoftDeletesAndCallsUpdate()
    {
        var now = DateTimeOffset.UtcNow;
        _clock.UtcNow.Returns(now);
        var voucher = CreatePlatformVoucher();
        _vouchers.FindPlatformByIdIgnoringSoftDeleteAsync(voucher.Id, Arg.Any<CancellationToken>())
            .Returns(voucher);

        var result = await BuildSut().Handle(new DeleteAdminVoucherCommand(voucher.Id), CancellationToken.None);

        result.Id.Should().Be(voucher.Id);
        result.DeletedAt.Should().Be(now);
        voucher.DeletedAt.Should().Be(now);
        _vouchers.Received(1).Update(voucher);
    }

    [Fact]
    public async Task Handle_AlreadySoftDeletedPlatformVoucher_ReturnsExistingDeletedAt()
    {
        var deletedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var voucher = CreatePlatformVoucher();
        voucher.SoftDelete(deletedAt);
        _vouchers.FindPlatformByIdIgnoringSoftDeleteAsync(voucher.Id, Arg.Any<CancellationToken>())
            .Returns(voucher);

        var result = await BuildSut().Handle(new DeleteAdminVoucherCommand(voucher.Id), CancellationToken.None);

        result.DeletedAt.Should().Be(deletedAt);
        _vouchers.DidNotReceive().Update(Arg.Any<Voucher>());
    }

    [Fact]
    public async Task Handle_OperatorOwnedVoucher_ThrowsVoucherNotFound()
    {
        var voucherId = Guid.NewGuid();
        _vouchers.FindPlatformByIdIgnoringSoftDeleteAsync(voucherId, Arg.Any<CancellationToken>())
            .Returns((Voucher?)null);

        var act = () => BuildSut().Handle(new DeleteAdminVoucherCommand(voucherId), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<CodedNotFoundException>();
        ex.Which.ErrorCode.Should().Be("VOUCHER_NOT_FOUND");
    }

    private static Voucher CreatePlatformVoucher() =>
        Voucher.Create(
            code: "DELADM01",
            name: "Delete Admin Voucher",
            type: VoucherType.FIXED_AMOUNT,
            value: 10_000,
            minOrderAmount: Money.FromRaw(50_000),
            maxDiscountAmount: null,
            totalUsageLimit: 100,
            perUserLimit: 1,
            validFrom: DateTimeOffset.UtcNow.AddDays(-1),
            validUntil: DateTimeOffset.UtcNow.AddDays(30),
            applicableOperatorIds: null,
            applicableRouteIds: null,
            fundingType: VoucherFundingType.VIETRIDE_FUNDED,
            ownerOperatorId: null,
            createdByUserId: AdminUserId);
}
