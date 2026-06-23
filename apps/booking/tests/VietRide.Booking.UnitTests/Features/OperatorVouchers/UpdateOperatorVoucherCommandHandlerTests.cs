using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.OperatorVouchers.UpdateOperatorVoucher;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.UnitTests.Features.OperatorVouchers;

/// <summary>
/// Unit tests for <see cref="UpdateOperatorVoucherCommandHandler"/> — freeze-on-first-use (Q6)
/// and tenant-isolation behaviour.
/// </summary>
public class UpdateOperatorVoucherCommandHandlerTests
{
    // -----------------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------------

    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherOperatorId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OperatorUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly IVoucherRepository _vouchers = Substitute.For<IVoucherRepository>();

    private UpdateOperatorVoucherCommandHandler BuildSut() => new(
        _vouchers,
        NullLogger<UpdateOperatorVoucherCommandHandler>.Instance);

    private static Voucher CreateOwnerVoucher(long value = 10_000) =>
        Voucher.Create(
            code: "OPCODE01",
            name: "Operator Voucher",
            type: VoucherType.FIXED_AMOUNT,
            value: value,
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

    /// <summary>
    /// Builds an UpdateOperatorVoucherCommand. When <paramref name="voucher"/> is provided,
    /// its ValidFrom/ValidUntil are used as defaults so that the "frozen dates unchanged" path
    /// is the default; pass explicit overrides to test shortening/changing behaviour.
    /// </summary>
    private static UpdateOperatorVoucherCommand BuildUpdateCommand(
        Guid? voucherId = null,
        Guid? callerOperatorId = null,
        long value = 10_000,
        long minOrderAmount = 50_000,
        long? maxDiscountAmount = null,
        string name = "Updated Name",
        Voucher? voucher = null,
        DateTimeOffset? validFrom = null,
        DateTimeOffset? validUntil = null,
        int? totalUsageLimit = null,
        int? perUserLimit = null) =>
        new(
            VoucherId: voucherId ?? voucher?.Id ?? Guid.NewGuid(),
            CallerOperatorId: callerOperatorId ?? OperatorId,
            Name: name,
            Value: value,
            MinOrderAmount: minOrderAmount,
            MaxDiscountAmount: maxDiscountAmount,
            TotalUsageLimit: totalUsageLimit,
            PerUserLimit: perUserLimit,
            ValidFrom: validFrom ?? voucher?.ValidFrom ?? DateTimeOffset.UtcNow.AddDays(-1),
            ValidUntil: validUntil ?? voucher?.ValidUntil ?? DateTimeOffset.UtcNow.AddDays(60),
            ApplicableRouteIds: null);

    // -----------------------------------------------------------------------
    // Tenant isolation — cross-operator access → 404 VOUCHER_NOT_FOUND
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_CrossOperatorAccess_ThrowsVoucherNotFound()
    {
        // Arrange
        var voucherId = Guid.NewGuid();
        _vouchers.FindByIdAndOwnerAsync(voucherId, OtherOperatorId, Arg.Any<CancellationToken>())
            .Returns((Voucher?)null);

        var sut = BuildSut();
        var command = BuildUpdateCommand(voucherId: voucherId, callerOperatorId: OtherOperatorId);

        // Act
        var act = () => sut.Handle(command, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<CodedNotFoundException>();
        ex.Which.ErrorCode.Should().Be("VOUCHER_NOT_FOUND");
    }

    // -----------------------------------------------------------------------
    // Freeze-on-first-use (Q6) — used voucher: editing value → VOUCHER_LOCKED
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_UsedVoucher_EditingValue_ThrowsVoucherLocked()
    {
        // Arrange
        var voucher = CreateOwnerVoucher(value: 10_000);
        _vouchers.FindByIdAndOwnerAsync(voucher.Id, OperatorId, Arg.Any<CancellationToken>())
            .Returns(voucher);
        _vouchers.CountUsagesAsync(voucher.Id, Arg.Any<CancellationToken>())
            .Returns(1); // has 1 usage — frozen

        var sut = BuildSut();
        // Attempt to change value from 10_000 to 20_000; supply voucher so ValidFrom/ValidUntil stay frozen-unchanged
        var command = BuildUpdateCommand(voucherId: voucher.Id, value: 20_000, minOrderAmount: 50_000, voucher: voucher);

        // Act
        var act = () => sut.Handle(command, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<CodedConflictException>();
        ex.Which.ErrorCode.Should().Be("VOUCHER_LOCKED");
    }

    // -----------------------------------------------------------------------
    // Freeze-on-first-use (Q6) — used voucher: editing name → allowed
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_UsedVoucher_EditingName_Succeeds()
    {
        // Arrange
        var voucher = CreateOwnerVoucher(value: 10_000);
        _vouchers.FindByIdAndOwnerAsync(voucher.Id, OperatorId, Arg.Any<CancellationToken>())
            .Returns(voucher);
        _vouchers.CountUsagesAsync(voucher.Id, Arg.Any<CancellationToken>())
            .Returns(1); // has 1 usage — frozen

        var sut = BuildSut();
        // Same economic fields (no change to value/minOrderAmount/maxDiscountAmount), only name changed;
        // supply voucher so ValidFrom/ValidUntil default to voucher's own frozen values
        var command = BuildUpdateCommand(
            voucherId: voucher.Id,
            value: 10_000,       // unchanged
            minOrderAmount: 50_000, // unchanged
            maxDiscountAmount: null,
            name: "New Name",
            voucher: voucher);

        // Act
        var result = await sut.Handle(command, CancellationToken.None);

        // Assert — handler returns updated result without throwing
        result.Name.Should().Be("New Name");
        result.Value.Should().Be(10_000); // economic field unchanged
    }

    // -----------------------------------------------------------------------
    // Freeze-on-first-use (Q6) — zero-usage voucher: editing value → allowed
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_ZeroUsageVoucher_EditingValue_Succeeds()
    {
        // Arrange
        var voucher = CreateOwnerVoucher(value: 10_000);
        _vouchers.FindByIdAndOwnerAsync(voucher.Id, OperatorId, Arg.Any<CancellationToken>())
            .Returns(voucher);
        _vouchers.CountUsagesAsync(voucher.Id, Arg.Any<CancellationToken>())
            .Returns(0); // no usages — not frozen

        var sut = BuildSut();
        // Zero-usage voucher: not locked, ValidFrom/ValidUntil from the voucher are not frozen,
        // so any valid date range is accepted. Supply voucher to ensure date defaults are valid.
        var command = BuildUpdateCommand(
            voucherId: voucher.Id,
            value: 20_000, // changed — allowed because zero usages
            minOrderAmount: 50_000,
            voucher: voucher);

        // Act
        var result = await sut.Handle(command, CancellationToken.None);

        // Assert
        result.Value.Should().Be(20_000);
    }

    // -----------------------------------------------------------------------
    // Freeze-on-first-use (Q6) — locked: shortening validUntil → VOUCHER_LOCKED
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_UsedVoucher_ShorteningValidUntil_ThrowsVoucherLocked()
    {
        // Arrange
        var voucher = CreateOwnerVoucher(value: 10_000);
        _vouchers.FindByIdAndOwnerAsync(voucher.Id, OperatorId, Arg.Any<CancellationToken>())
            .Returns(voucher);
        _vouchers.CountUsagesAsync(voucher.Id, Arg.Any<CancellationToken>())
            .Returns(1); // locked

        var sut = BuildSut();

        // Send the same economic fields but a validUntil BEFORE the current one
        var shorterValidUntil = voucher.ValidUntil.AddDays(-5); // shorter → rejected
        var command = new UpdateOperatorVoucherCommand(
            VoucherId: voucher.Id,
            CallerOperatorId: OperatorId,
            Name: voucher.Name,
            Value: voucher.Value,          // unchanged
            MinOrderAmount: voucher.MinOrderAmount.Amount, // unchanged
            MaxDiscountAmount: voucher.MaxDiscountAmount?.Amount,
            TotalUsageLimit: voucher.TotalUsageLimit,
            PerUserLimit: voucher.PerUserLimit,
            ValidFrom: voucher.ValidFrom,  // unchanged (frozen)
            ValidUntil: shorterValidUntil, // shortened — must reject
            ApplicableRouteIds: null);

        // Act
        var act = () => sut.Handle(command, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<CodedConflictException>();
        ex.Which.ErrorCode.Should().Be("VOUCHER_LOCKED");
    }

    // -----------------------------------------------------------------------
    // Freeze-on-first-use (Q6) — locked: changing validFrom → VOUCHER_LOCKED
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_UsedVoucher_ChangingValidFrom_ThrowsVoucherLocked()
    {
        // Arrange
        var voucher = CreateOwnerVoucher(value: 10_000);
        _vouchers.FindByIdAndOwnerAsync(voucher.Id, OperatorId, Arg.Any<CancellationToken>())
            .Returns(voucher);
        _vouchers.CountUsagesAsync(voucher.Id, Arg.Any<CancellationToken>())
            .Returns(1); // locked

        var sut = BuildSut();

        // Attempt to change validFrom while locked
        var differentValidFrom = voucher.ValidFrom.AddDays(1); // changed — must reject
        var command = new UpdateOperatorVoucherCommand(
            VoucherId: voucher.Id,
            CallerOperatorId: OperatorId,
            Name: voucher.Name,
            Value: voucher.Value,
            MinOrderAmount: voucher.MinOrderAmount.Amount,
            MaxDiscountAmount: voucher.MaxDiscountAmount?.Amount,
            TotalUsageLimit: voucher.TotalUsageLimit,
            PerUserLimit: voucher.PerUserLimit,
            ValidFrom: differentValidFrom, // changed — must reject
            ValidUntil: voucher.ValidUntil,
            ApplicableRouteIds: null);

        // Act
        var act = () => sut.Handle(command, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<CodedConflictException>();
        ex.Which.ErrorCode.Should().Be("VOUCHER_LOCKED");
    }

    // -----------------------------------------------------------------------
    // Freeze-on-first-use (Q6) — locked: extending validUntil → allowed
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_UsedVoucher_ExtendingValidUntil_Succeeds()
    {
        // Arrange
        var voucher = CreateOwnerVoucher(value: 10_000);
        _vouchers.FindByIdAndOwnerAsync(voucher.Id, OperatorId, Arg.Any<CancellationToken>())
            .Returns(voucher);
        _vouchers.CountUsagesAsync(voucher.Id, Arg.Any<CancellationToken>())
            .Returns(1); // locked

        var sut = BuildSut();

        var extendedValidUntil = voucher.ValidUntil.AddDays(10); // extended — allowed
        var command = new UpdateOperatorVoucherCommand(
            VoucherId: voucher.Id,
            CallerOperatorId: OperatorId,
            Name: voucher.Name,
            Value: voucher.Value,
            MinOrderAmount: voucher.MinOrderAmount.Amount,
            MaxDiscountAmount: voucher.MaxDiscountAmount?.Amount,
            TotalUsageLimit: voucher.TotalUsageLimit,
            PerUserLimit: voucher.PerUserLimit,
            ValidFrom: voucher.ValidFrom,   // frozen but unchanged
            ValidUntil: extendedValidUntil, // extended — allowed
            ApplicableRouteIds: null);

        // Act
        var result = await sut.Handle(command, CancellationToken.None);

        // Assert — extension is allowed
        result.ValidUntil.Should().Be(extendedValidUntil);
    }

    // -----------------------------------------------------------------------
    // Freeze-on-first-use (Q6) — locked: tightening totalUsageLimit → VOUCHER_LOCKED
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_UsedVoucher_TighteningTotalUsageLimit_ThrowsVoucherLocked()
    {
        // Arrange — voucher has TotalUsageLimit = 100
        var voucher = CreateOwnerVoucher(value: 10_000);
        _vouchers.FindByIdAndOwnerAsync(voucher.Id, OperatorId, Arg.Any<CancellationToken>())
            .Returns(voucher);
        _vouchers.CountUsagesAsync(voucher.Id, Arg.Any<CancellationToken>())
            .Returns(1); // locked

        var sut = BuildSut();

        // Attempt to REDUCE the limit from 100 to 50 — tightening → rejected
        var command = new UpdateOperatorVoucherCommand(
            VoucherId: voucher.Id,
            CallerOperatorId: OperatorId,
            Name: voucher.Name,
            Value: voucher.Value,
            MinOrderAmount: voucher.MinOrderAmount.Amount,
            MaxDiscountAmount: voucher.MaxDiscountAmount?.Amount,
            TotalUsageLimit: 50, // tightened (was 100) — must reject
            PerUserLimit: voucher.PerUserLimit,
            ValidFrom: voucher.ValidFrom,
            ValidUntil: voucher.ValidUntil,
            ApplicableRouteIds: null);

        // Act
        var act = () => sut.Handle(command, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<CodedConflictException>();
        ex.Which.ErrorCode.Should().Be("VOUCHER_LOCKED");
    }

    // -----------------------------------------------------------------------
    // Freeze-on-first-use (Q6) — locked: null → finite is a tightening → VOUCHER_LOCKED
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_UsedVoucher_NullToFiniteLimit_ThrowsVoucherLocked()
    {
        // Arrange — voucher with no limit (TotalUsageLimit = null = unlimited)
        var voucher = Voucher.Create(
            code: "OPNOLIMT",
            name: "No Limit Voucher",
            type: VoucherType.FIXED_AMOUNT,
            value: 10_000,
            minOrderAmount: Money.FromRaw(50_000),
            maxDiscountAmount: null,
            totalUsageLimit: null,  // unlimited
            perUserLimit: null,
            validFrom: DateTimeOffset.UtcNow.AddDays(-1),
            validUntil: DateTimeOffset.UtcNow.AddDays(30),
            applicableOperatorIds: [OperatorId],
            applicableRouteIds: null,
            fundingType: VoucherFundingType.OPERATOR_FUNDED,
            ownerOperatorId: OperatorId,
            createdByUserId: OperatorUserId);

        _vouchers.FindByIdAndOwnerAsync(voucher.Id, OperatorId, Arg.Any<CancellationToken>())
            .Returns(voucher);
        _vouchers.CountUsagesAsync(voucher.Id, Arg.Any<CancellationToken>())
            .Returns(1); // locked

        var sut = BuildSut();

        // Attempt to set a finite limit on previously unlimited voucher — tightening → rejected
        var command = new UpdateOperatorVoucherCommand(
            VoucherId: voucher.Id,
            CallerOperatorId: OperatorId,
            Name: voucher.Name,
            Value: voucher.Value,
            MinOrderAmount: voucher.MinOrderAmount.Amount,
            MaxDiscountAmount: voucher.MaxDiscountAmount?.Amount,
            TotalUsageLimit: 200, // null → finite = tightening — must reject
            PerUserLimit: null,
            ValidFrom: voucher.ValidFrom,
            ValidUntil: voucher.ValidUntil,
            ApplicableRouteIds: null);

        // Act
        var act = () => sut.Handle(command, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<CodedConflictException>();
        ex.Which.ErrorCode.Should().Be("VOUCHER_LOCKED");
    }

    // -----------------------------------------------------------------------
    // Freeze-on-first-use (Q6) — locked: loosening limit (finite → null) → allowed
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_UsedVoucher_LoosenLimitToUnlimited_Succeeds()
    {
        // Arrange — voucher has TotalUsageLimit = 100
        var voucher = CreateOwnerVoucher(value: 10_000);
        _vouchers.FindByIdAndOwnerAsync(voucher.Id, OperatorId, Arg.Any<CancellationToken>())
            .Returns(voucher);
        _vouchers.CountUsagesAsync(voucher.Id, Arg.Any<CancellationToken>())
            .Returns(1); // locked

        var sut = BuildSut();

        // Set limit to null (unlimited) — loosening → allowed
        var command = new UpdateOperatorVoucherCommand(
            VoucherId: voucher.Id,
            CallerOperatorId: OperatorId,
            Name: voucher.Name,
            Value: voucher.Value,
            MinOrderAmount: voucher.MinOrderAmount.Amount,
            MaxDiscountAmount: voucher.MaxDiscountAmount?.Amount,
            TotalUsageLimit: null, // finite → null = loosening — allowed
            PerUserLimit: voucher.PerUserLimit,
            ValidFrom: voucher.ValidFrom,
            ValidUntil: voucher.ValidUntil,
            ApplicableRouteIds: null);

        // Act
        var result = await sut.Handle(command, CancellationToken.None);

        // Assert — null = "keep current"; the limit is still 100 (null is not a set-to-unlimited signal).
        // Explicit loosening via a provided numeric value (covered by Handle_UsedVoucher_LoosenLimitByIncrease_Succeeds)
        // still works because the PATCH sends an actual value, not null.
        result.Should().NotBeNull();
        voucher.TotalUsageLimit.Should().Be(100,
            "null TotalUsageLimit means 'keep current' — it must NOT silently loosen to unlimited");
    }

    // -----------------------------------------------------------------------
    // Explicit loosening via a provided value (200 > 100) still works
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_UsedVoucher_LoosenLimitByIncrease_Succeeds()
    {
        // Arrange — voucher has TotalUsageLimit = 100
        var voucher = CreateOwnerVoucher(value: 10_000);  // TotalUsageLimit = 100
        _vouchers.FindByIdAndOwnerAsync(voucher.Id, OperatorId, Arg.Any<CancellationToken>())
            .Returns(voucher);
        _vouchers.CountUsagesAsync(voucher.Id, Arg.Any<CancellationToken>())
            .Returns(1); // locked

        var sut = BuildSut();

        // Provide TotalUsageLimit = 200 (explicitly larger than 100) — valid loosening
        var command = new UpdateOperatorVoucherCommand(
            VoucherId: voucher.Id,
            CallerOperatorId: OperatorId,
            Name: voucher.Name,
            Value: voucher.Value,
            MinOrderAmount: voucher.MinOrderAmount.Amount,
            MaxDiscountAmount: voucher.MaxDiscountAmount?.Amount,
            TotalUsageLimit: 200, // finite → larger finite = loosening — allowed
            PerUserLimit: voucher.PerUserLimit,
            ValidFrom: voucher.ValidFrom,
            ValidUntil: voucher.ValidUntil,
            ApplicableRouteIds: null);

        // Act
        var result = await sut.Handle(command, CancellationToken.None);

        // Assert — explicit increase is applied
        result.Should().NotBeNull();
        voucher.TotalUsageLimit.Should().Be(200,
            "explicitly providing a larger TotalUsageLimit is a valid loosening and must be applied");
    }

    // -----------------------------------------------------------------------
    // Omitting TotalUsageLimit (null) KEEPS the current value
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_OmitTotalUsageLimit_KeepsCurrentValue()
    {
        // Arrange — voucher has TotalUsageLimit = 100
        var voucher = CreateOwnerVoucher(value: 10_000);  // TotalUsageLimit = 100
        _vouchers.FindByIdAndOwnerAsync(voucher.Id, OperatorId, Arg.Any<CancellationToken>())
            .Returns(voucher);
        _vouchers.CountUsagesAsync(voucher.Id, Arg.Any<CancellationToken>())
            .Returns(0); // not locked — omit-to-keep also matters for zero-usage path

        var sut = BuildSut();

        // Send TotalUsageLimit = null (omitted / "keep current") — must NOT change to unlimited
        var command = new UpdateOperatorVoucherCommand(
            VoucherId: voucher.Id,
            CallerOperatorId: OperatorId,
            Name: voucher.Name,
            Value: voucher.Value,
            MinOrderAmount: voucher.MinOrderAmount.Amount,
            MaxDiscountAmount: voucher.MaxDiscountAmount?.Amount,
            TotalUsageLimit: null,  // omitted = keep current (100)
            PerUserLimit: null,     // omitted = keep current (1)
            ValidFrom: voucher.ValidFrom,
            ValidUntil: voucher.ValidUntil,
            ApplicableRouteIds: null);

        // Act
        var result = await sut.Handle(command, CancellationToken.None);

        // Assert — TotalUsageLimit must still be 100, NOT null (unlimited)
        result.Should().NotBeNull();
        voucher.TotalUsageLimit.Should().Be(100,
            "omitting TotalUsageLimit (null) means keep current — it must not silently become unlimited");
        voucher.PerUserLimit.Should().Be(1,
            "omitting PerUserLimit (null) means keep current — it must not silently become unlimited");
    }

    // -----------------------------------------------------------------------
    // Omitting ApplicableRouteIds (null) KEEPS existing route restrictions
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_OmitApplicableRouteIds_KeepsExistingRouteRestrictions()
    {
        // Arrange — voucher with two route restrictions
        var routeId1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var routeId2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var voucher = Voucher.Create(
            code: "ROUTELVCH",
            name: "Route-restricted Voucher",
            type: VoucherType.FIXED_AMOUNT,
            value: 10_000,
            minOrderAmount: Money.FromRaw(50_000),
            maxDiscountAmount: null,
            totalUsageLimit: 50,
            perUserLimit: 1,
            validFrom: DateTimeOffset.UtcNow.AddDays(-1),
            validUntil: DateTimeOffset.UtcNow.AddDays(30),
            applicableOperatorIds: [OperatorId],
            applicableRouteIds: [routeId1, routeId2],
            fundingType: VoucherFundingType.OPERATOR_FUNDED,
            ownerOperatorId: OperatorId,
            createdByUserId: OperatorUserId);

        _vouchers.FindByIdAndOwnerAsync(voucher.Id, OperatorId, Arg.Any<CancellationToken>())
            .Returns(voucher);
        _vouchers.CountUsagesAsync(voucher.Id, Arg.Any<CancellationToken>())
            .Returns(0); // not locked

        var sut = BuildSut();

        // Send ApplicableRouteIds = null (omitted = "keep current") — must NOT clear restrictions
        var command = new UpdateOperatorVoucherCommand(
            VoucherId: voucher.Id,
            CallerOperatorId: OperatorId,
            Name: "New Name",
            Value: voucher.Value,
            MinOrderAmount: voucher.MinOrderAmount.Amount,
            MaxDiscountAmount: voucher.MaxDiscountAmount?.Amount,
            TotalUsageLimit: voucher.TotalUsageLimit,
            PerUserLimit: voucher.PerUserLimit,
            ValidFrom: voucher.ValidFrom,
            ValidUntil: voucher.ValidUntil,
            ApplicableRouteIds: null);  // omitted = keep current restrictions

        // Act
        var result = await sut.Handle(command, CancellationToken.None);

        // Assert — route restrictions must be preserved
        result.Should().NotBeNull();
        voucher.ApplicableRouteIds.Should().BeEquivalentTo(new[] { routeId1, routeId2 },
            "omitting ApplicableRouteIds (null) means keep current — must not clear route restrictions");
    }
}
