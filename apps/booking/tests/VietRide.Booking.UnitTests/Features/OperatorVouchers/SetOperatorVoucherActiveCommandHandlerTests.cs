using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.OperatorVouchers.SetOperatorVoucherActive;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.UnitTests.Features.OperatorVouchers;

/// <summary>
/// Unit tests for <see cref="SetOperatorVoucherActiveCommandHandler"/>:
/// (a) activate flips IsActive to true;
/// (b) deactivate flips IsActive to false;
/// (c) cross-operator access → 404 VOUCHER_NOT_FOUND (tenant isolation).
/// </summary>
public class SetOperatorVoucherActiveCommandHandlerTests
{
    // -----------------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------------

    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherOperatorId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OperatorUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly IVoucherRepository _vouchers = Substitute.For<IVoucherRepository>();

    private SetOperatorVoucherActiveCommandHandler BuildSut() => new(
        _vouchers,
        NullLogger<SetOperatorVoucherActiveCommandHandler>.Instance);

    private static Voucher CreateOwnerVoucher() =>
        Voucher.Create(
            code: "ACTCODE1",
            name: "Activation Test Voucher",
            type: VoucherType.FIXED_AMOUNT,
            value: 10_000,
            minOrderAmount: Money.FromRaw(50_000),
            maxDiscountAmount: null,
            totalUsageLimit: 100,
            perUserLimit: 1,
            validFrom: DateTimeOffset.UtcNow.AddDays(-1),
            validUntil: DateTimeOffset.UtcNow.AddDays(30),
            applicableOperatorIds: [OperatorId],
            applicableRouteIds: null,
            fundingType: VoucherFundingType.OPERATOR_FUNDED,
            ownerOperatorId: OperatorId,
            createdByUserId: OperatorUserId);

    // -----------------------------------------------------------------------
    // Happy path — activate flips IsActive to true
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_ActivateVoucher_FlipsIsActiveTrue()
    {
        // Arrange
        var voucher = CreateOwnerVoucher();

        // Ensure it starts deactivated so the flip is visible
        voucher.Deactivate();
        voucher.IsActive.Should().BeFalse("pre-condition: voucher must start inactive");

        _vouchers.FindByIdAndOwnerAsync(voucher.Id, OperatorId, Arg.Any<CancellationToken>())
            .Returns(voucher);

        var sut = BuildSut();
        var command = new SetOperatorVoucherActiveCommand(voucher.Id, OperatorId, Activate: true);

        // Act
        var result = await sut.Handle(command, CancellationToken.None);

        // Assert
        result.Id.Should().Be(voucher.Id);
        result.IsActive.Should().BeTrue();
        voucher.IsActive.Should().BeTrue();
        _vouchers.Received(1).Update(voucher);
    }

    // -----------------------------------------------------------------------
    // Happy path — deactivate flips IsActive to false
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_DeactivateVoucher_FlipsIsActiveFalse()
    {
        // Arrange
        var voucher = CreateOwnerVoucher();
        // Vouchers start active by default; confirm.
        voucher.IsActive.Should().BeTrue("pre-condition: voucher must start active");

        _vouchers.FindByIdAndOwnerAsync(voucher.Id, OperatorId, Arg.Any<CancellationToken>())
            .Returns(voucher);

        var sut = BuildSut();
        var command = new SetOperatorVoucherActiveCommand(voucher.Id, OperatorId, Activate: false);

        // Act
        var result = await sut.Handle(command, CancellationToken.None);

        // Assert
        result.Id.Should().Be(voucher.Id);
        result.IsActive.Should().BeFalse();
        voucher.IsActive.Should().BeFalse();
        _vouchers.Received(1).Update(voucher);
    }

    // -----------------------------------------------------------------------
    // Tenant isolation — cross-operator access → 404 VOUCHER_NOT_FOUND
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_CrossOperatorAccess_ThrowsVoucherNotFound()
    {
        // Arrange — repository returns null for a voucher owned by a different operator
        var voucherId = Guid.NewGuid();
        _vouchers.FindByIdAndOwnerAsync(voucherId, OtherOperatorId, Arg.Any<CancellationToken>())
            .Returns((Voucher?)null);

        var sut = BuildSut();
        var command = new SetOperatorVoucherActiveCommand(voucherId, OtherOperatorId, Activate: true);

        // Act
        var act = () => sut.Handle(command, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<CodedNotFoundException>();
        ex.Which.ErrorCode.Should().Be("VOUCHER_NOT_FOUND");
    }
}
