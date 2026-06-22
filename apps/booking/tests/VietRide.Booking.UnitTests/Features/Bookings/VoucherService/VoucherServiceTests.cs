using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Services;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.UnitTests.Features.Bookings.VoucherService;

/// <summary>
/// Unit tests for <see cref="VietRide.Booking.Application.Services.VoucherService"/>.
/// Covers the canonical Q8 validation order, applicability branches (a)/(b), discount
/// computation, and usage-limit boundary conditions required by the Task 14.3 acceptance criteria.
/// </summary>
public class VoucherServiceTests
{
    // -----------------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------------

    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherOperatorId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid UserId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid BookingId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid RouteId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid OtherRouteId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    private static readonly DateTimeOffset Now = new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly IVoucherRepository _vouchers = Substitute.For<IVoucherRepository>();
    private readonly IOperatorVoucherConsentRepository _consents = Substitute.For<IOperatorVoucherConsentRepository>();

    private Application.Services.VoucherService BuildSut() => new(
        _vouchers,
        _consents,
        NullLogger<Application.Services.VoucherService>.Instance);

    /// <summary>Creates a fully valid active platform VIETRIDE_FUNDED voucher.</summary>
    private static Voucher CreatePlatformVoucher(
        string code = "DISCOUNT10",
        VoucherType type = VoucherType.PERCENT_OFF,
        long value = 10,
        long minOrderAmount = 50_000,
        long? maxDiscountAmount = null,
        int? totalUsageLimit = null,
        int? perUserLimit = null,
        List<Guid>? applicableOperatorIds = null,
        List<Guid>? applicableRouteIds = null,
        VoucherFundingType fundingType = VoucherFundingType.VIETRIDE_FUNDED)
        => Voucher.Create(
            code: code,
            name: "Platform Voucher",
            type: type,
            value: value,
            minOrderAmount: Money.FromRaw(minOrderAmount),
            maxDiscountAmount: maxDiscountAmount.HasValue ? Money.FromRaw(maxDiscountAmount.Value) : null,
            totalUsageLimit: totalUsageLimit,
            perUserLimit: perUserLimit,
            validFrom: Now.AddDays(-1),
            validUntil: Now.AddDays(30),
            applicableOperatorIds: applicableOperatorIds,
            applicableRouteIds: applicableRouteIds,
            fundingType: fundingType,
            ownerOperatorId: null,
            createdByUserId: Guid.NewGuid());

    /// <summary>Creates a fully valid active operator-owned OPERATOR_FUNDED voucher.</summary>
    private static Voucher CreateOperatorOwnedVoucher(
        Guid? ownerOperatorId = null,
        string code = "OPVCH001",
        VoucherType type = VoucherType.FIXED_AMOUNT,
        long value = 20_000)
        => Voucher.Create(
            code: code,
            name: "Operator Voucher",
            type: type,
            value: value,
            minOrderAmount: Money.FromRaw(50_000),
            maxDiscountAmount: null,
            totalUsageLimit: null,
            perUserLimit: null,
            validFrom: Now.AddDays(-1),
            validUntil: Now.AddDays(30),
            applicableOperatorIds: [ownerOperatorId ?? OperatorId],
            applicableRouteIds: null,
            fundingType: VoucherFundingType.OPERATOR_FUNDED,
            ownerOperatorId: ownerOperatorId ?? OperatorId,
            createdByUserId: Guid.NewGuid());

    // -----------------------------------------------------------------------
    // Happy path — VIETRIDE_FUNDED platform voucher, PERCENT_OFF
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidateAndComputeDiscount_PlatformVietrideFunded_ReturnsCorrectDiscount()
    {
        var voucher = CreatePlatformVoucher(value: 10); // 10%
        _vouchers.FindByCodeAsync("DISCOUNT10", Arg.Any<CancellationToken>()).Returns(voucher);
        _vouchers.CountUsagesAsync(voucher.Id, Arg.Any<CancellationToken>()).Returns(0);
        _vouchers.CountUsagesByUserAsync(voucher.Id, UserId, Arg.Any<CancellationToken>()).Returns(0);

        var sut = BuildSut();
        var result = await sut.ValidateAndComputeDiscountAsync(
            "DISCOUNT10", OperatorId, RouteId, UserId,
            orderAmount: Money.FromRaw(200_000),
            now: Now);

        result.Discount.Amount.Should().Be(20_000); // 10% of 200_000
        result.VoucherId.Should().Be(voucher.Id);
    }

    // -----------------------------------------------------------------------
    // Happy path — operator-owned voucher (branch a) — no consent check
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidateAndComputeDiscount_OperatorOwnedVoucher_SameOperator_SkipsConsentCheck()
    {
        var voucher = CreateOperatorOwnedVoucher(ownerOperatorId: OperatorId, value: 20_000);
        _vouchers.FindByCodeAsync("OPVCH001", Arg.Any<CancellationToken>()).Returns(voucher);

        var sut = BuildSut();
        var result = await sut.ValidateAndComputeDiscountAsync(
            "OPVCH001", OperatorId, RouteId, UserId,
            orderAmount: Money.FromRaw(200_000),
            now: Now);

        result.Discount.Amount.Should().Be(20_000);
        // Consent repository MUST NOT be called for operator-owned vouchers
        await _consents.DidNotReceiveWithAnyArgs()
            .FindAcceptedByVoucherAndOperatorAsync(default, default, default);
    }

    // -----------------------------------------------------------------------
    // Error: operator-owned voucher applied to a DIFFERENT operator → VOUCHER_NOT_APPLICABLE
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidateAndComputeDiscount_OperatorOwnedVoucher_WrongOperator_ThrowsNotApplicable()
    {
        var voucher = CreateOperatorOwnedVoucher(ownerOperatorId: OperatorId);
        _vouchers.FindByCodeAsync("OPVCH001", Arg.Any<CancellationToken>()).Returns(voucher);

        var sut = BuildSut();
        var act = () => sut.ValidateAndComputeDiscountAsync(
            "OPVCH001", OtherOperatorId, RouteId, UserId,
            orderAmount: Money.FromRaw(200_000),
            now: Now);

        var ex = await act.Should().ThrowAsync<CodedValidationException>();
        ex.Which.ErrorCode.Should().Be("VOUCHER_NOT_APPLICABLE");
    }

    // -----------------------------------------------------------------------
    // Error: admin OPERATOR_FUNDED platform voucher without ACCEPTED consent → VOUCHER_NOT_APPLICABLE
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidateAndComputeDiscount_AdminOperatorFunded_NoAcceptedConsent_ThrowsNotApplicable()
    {
        var voucher = CreatePlatformVoucher(
            fundingType: VoucherFundingType.OPERATOR_FUNDED,
            applicableOperatorIds: [OperatorId]);
        _vouchers.FindByCodeAsync("DISCOUNT10", Arg.Any<CancellationToken>()).Returns(voucher);
        _consents.FindAcceptedByVoucherAndOperatorAsync(voucher.Id, OperatorId, Arg.Any<CancellationToken>())
            .Returns((OperatorVoucherConsent?)null);

        var sut = BuildSut();
        var act = () => sut.ValidateAndComputeDiscountAsync(
            "DISCOUNT10", OperatorId, RouteId, UserId,
            orderAmount: Money.FromRaw(200_000),
            now: Now);

        var ex = await act.Should().ThrowAsync<CodedValidationException>();
        ex.Which.ErrorCode.Should().Be("VOUCHER_NOT_APPLICABLE");
    }

    // -----------------------------------------------------------------------
    // Error: voucher not found → VOUCHER_NOT_FOUND
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidateAndComputeDiscount_VoucherNotFound_ThrowsVoucherNotFound()
    {
        _vouchers.FindByCodeAsync("NOSUCH", Arg.Any<CancellationToken>()).Returns((Voucher?)null);

        var sut = BuildSut();
        var act = () => sut.ValidateAndComputeDiscountAsync(
            "NOSUCH", OperatorId, RouteId, UserId,
            orderAmount: Money.FromRaw(200_000),
            now: Now);

        var ex = await act.Should().ThrowAsync<CodedNotFoundException>();
        ex.Which.ErrorCode.Should().Be("VOUCHER_NOT_FOUND");
    }

    // -----------------------------------------------------------------------
    // Error: voucher is_active=false → VOUCHER_NOT_FOUND (indistinguishable from not-found)
    // Verifies BLOCKER-1 fix: inactive voucher must NOT surface VOUCHER_INACTIVE.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidateAndComputeDiscount_InactiveVoucher_ThrowsVoucherNotFound()
    {
        var voucher = CreatePlatformVoucher();
        voucher.Deactivate(); // sets IsActive = false via IActivatable
        _vouchers.FindByCodeAsync("DISCOUNT10", Arg.Any<CancellationToken>()).Returns(voucher);

        var sut = BuildSut();
        var act = () => sut.ValidateAndComputeDiscountAsync(
            "DISCOUNT10", OperatorId, RouteId, UserId,
            orderAmount: Money.FromRaw(200_000),
            now: Now);

        var ex = await act.Should().ThrowAsync<CodedNotFoundException>(
            "an inactive voucher must be indistinguishable from not-found (BLOCKER-1)");
        ex.Which.ErrorCode.Should().Be("VOUCHER_NOT_FOUND");
    }

    // -----------------------------------------------------------------------
    // Error: voucher expired → VOUCHER_EXPIRED
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidateAndComputeDiscount_ExpiredVoucher_ThrowsVoucherExpired()
    {
        var voucher = Voucher.Create(
            code: "EXPIRED",
            name: "Old Voucher",
            type: VoucherType.FIXED_AMOUNT,
            value: 10_000,
            minOrderAmount: Money.FromRaw(0),
            maxDiscountAmount: null,
            totalUsageLimit: null,
            perUserLimit: null,
            validFrom: Now.AddDays(-30),
            validUntil: Now.AddDays(-1), // expired yesterday
            applicableOperatorIds: null,
            applicableRouteIds: null,
            fundingType: VoucherFundingType.VIETRIDE_FUNDED,
            ownerOperatorId: null,
            createdByUserId: Guid.NewGuid());

        _vouchers.FindByCodeAsync("EXPIRED", Arg.Any<CancellationToken>()).Returns(voucher);

        var sut = BuildSut();
        var act = () => sut.ValidateAndComputeDiscountAsync(
            "EXPIRED", OperatorId, RouteId, UserId,
            orderAmount: Money.FromRaw(200_000),
            now: Now);

        var ex = await act.Should().ThrowAsync<CodedValidationException>();
        ex.Which.ErrorCode.Should().Be("VOUCHER_EXPIRED");
    }

    // -----------------------------------------------------------------------
    // Error: wrong route → VOUCHER_NOT_APPLICABLE
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidateAndComputeDiscount_WrongRoute_ThrowsNotApplicable()
    {
        var voucher = CreatePlatformVoucher(applicableRouteIds: [RouteId]);
        _vouchers.FindByCodeAsync("DISCOUNT10", Arg.Any<CancellationToken>()).Returns(voucher);

        var sut = BuildSut();
        var act = () => sut.ValidateAndComputeDiscountAsync(
            "DISCOUNT10", OperatorId, OtherRouteId, UserId,
            orderAmount: Money.FromRaw(200_000),
            now: Now);

        var ex = await act.Should().ThrowAsync<CodedValidationException>();
        ex.Which.ErrorCode.Should().Be("VOUCHER_NOT_APPLICABLE");
    }

    // -----------------------------------------------------------------------
    // Error: min-order not met → VOUCHER_MIN_ORDER_NOT_MET
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidateAndComputeDiscount_MinOrderNotMet_ThrowsMinOrderNotMet()
    {
        var voucher = CreatePlatformVoucher(minOrderAmount: 300_000); // min 300k
        _vouchers.FindByCodeAsync("DISCOUNT10", Arg.Any<CancellationToken>()).Returns(voucher);

        var sut = BuildSut();
        var act = () => sut.ValidateAndComputeDiscountAsync(
            "DISCOUNT10", OperatorId, RouteId, UserId,
            orderAmount: Money.FromRaw(200_000), // 200k < 300k
            now: Now);

        var ex = await act.Should().ThrowAsync<CodedValidationException>();
        ex.Which.ErrorCode.Should().Be("VOUCHER_MIN_ORDER_NOT_MET");
    }

    // -----------------------------------------------------------------------
    // Usage-limit boundary: Nth succeeds, (N+1)th → VOUCHER_USAGE_LIMIT_REACHED
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidateAndComputeDiscount_TotalLimitBoundary_NthSucceeds_NPlus1ThFails()
    {
        const int limit = 5;
        var voucher = CreatePlatformVoucher(totalUsageLimit: limit);
        _vouchers.FindByCodeAsync("DISCOUNT10", Arg.Any<CancellationToken>()).Returns(voucher);
        _vouchers.CountUsagesByUserAsync(voucher.Id, UserId, Arg.Any<CancellationToken>()).Returns(0);

        var sut = BuildSut();

        // Nth attempt (4 existing usages = one before limit) → should succeed
        _vouchers.CountUsagesAsync(voucher.Id, Arg.Any<CancellationToken>()).Returns(limit - 1);
        var resultN = await sut.ValidateAndComputeDiscountAsync(
            "DISCOUNT10", OperatorId, RouteId, UserId,
            orderAmount: Money.FromRaw(200_000),
            now: Now);
        resultN.Should().NotBeNull("the Nth usage should succeed");

        // (N+1)th attempt (limit usages already) → should fail
        _vouchers.CountUsagesAsync(voucher.Id, Arg.Any<CancellationToken>()).Returns(limit);
        var act = () => sut.ValidateAndComputeDiscountAsync(
            "DISCOUNT10", OperatorId, RouteId, UserId,
            orderAmount: Money.FromRaw(200_000),
            now: Now);
        var ex = await act.Should().ThrowAsync<CodedValidationException>();
        ex.Which.ErrorCode.Should().Be("VOUCHER_USAGE_LIMIT_REACHED");
    }

    // -----------------------------------------------------------------------
    // Per-user limit boundary → VOUCHER_USER_LIMIT_REACHED
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidateAndComputeDiscount_PerUserLimitReached_ThrowsUserLimitReached()
    {
        const int perUserLimit = 1;
        var voucher = CreatePlatformVoucher(perUserLimit: perUserLimit);
        _vouchers.FindByCodeAsync("DISCOUNT10", Arg.Any<CancellationToken>()).Returns(voucher);
        _vouchers.CountUsagesAsync(voucher.Id, Arg.Any<CancellationToken>()).Returns(0); // total limit not hit
        _vouchers.CountUsagesByUserAsync(voucher.Id, UserId, Arg.Any<CancellationToken>())
            .Returns(perUserLimit); // user already used it once

        var sut = BuildSut();
        var act = () => sut.ValidateAndComputeDiscountAsync(
            "DISCOUNT10", OperatorId, RouteId, UserId,
            orderAmount: Money.FromRaw(200_000),
            now: Now);

        var ex = await act.Should().ThrowAsync<CodedValidationException>();
        ex.Which.ErrorCode.Should().Be("VOUCHER_USER_LIMIT_REACHED");
    }

    // -----------------------------------------------------------------------
    // Discount computation — PERCENT_OFF with max_discount_amount cap
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidateAndComputeDiscount_PercentOff_CappedByMaxDiscount()
    {
        // 50% of 200k = 100k, but cap is 30k
        var voucher = CreatePlatformVoucher(
            value: 50,
            maxDiscountAmount: 30_000);
        _vouchers.FindByCodeAsync("DISCOUNT10", Arg.Any<CancellationToken>()).Returns(voucher);
        _vouchers.CountUsagesAsync(voucher.Id, Arg.Any<CancellationToken>()).Returns(0);
        _vouchers.CountUsagesByUserAsync(voucher.Id, UserId, Arg.Any<CancellationToken>()).Returns(0);

        var sut = BuildSut();
        var result = await sut.ValidateAndComputeDiscountAsync(
            "DISCOUNT10", OperatorId, RouteId, UserId,
            orderAmount: Money.FromRaw(200_000),
            now: Now);

        result.Discount.Amount.Should().Be(30_000, "discount capped at max_discount_amount");
    }

    // -----------------------------------------------------------------------
    // Discount computation — PERCENT_OFF rounding half-up (MidpointRounding.AwayFromZero)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(100_001, 10, 10_000)] // 10% of 100_001 = 10000.1 → rounds to 10000 (< 0.5 → down)
    [InlineData(100_005, 10, 10_001)] // 10% of 100_005 = 10000.5 → rounds to 10001 (= 0.5 → half-up)
    [InlineData(100_003, 10, 10_000)] // 10% of 100_003 = 10000.3 → rounds to 10000 (< 0.5 → down)
    public async Task ValidateAndComputeDiscount_PercentOff_RoundsHalfUp(
        long orderAmount, long percent, long expectedDiscount)
    {
        var voucher = CreatePlatformVoucher(type: VoucherType.PERCENT_OFF, value: percent);
        _vouchers.FindByCodeAsync("DISCOUNT10", Arg.Any<CancellationToken>()).Returns(voucher);
        _vouchers.CountUsagesAsync(voucher.Id, Arg.Any<CancellationToken>()).Returns(0);
        _vouchers.CountUsagesByUserAsync(voucher.Id, UserId, Arg.Any<CancellationToken>()).Returns(0);

        var sut = BuildSut();
        var result = await sut.ValidateAndComputeDiscountAsync(
            "DISCOUNT10", OperatorId, RouteId, UserId,
            orderAmount: Money.FromRaw(orderAmount),
            now: Now);

        result.Discount.Amount.Should().Be(expectedDiscount);
    }

    // -----------------------------------------------------------------------
    // RecordUsageAsync — writes usage row and returns id
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RecordUsage_WritesUsageRowAndReturnsId()
    {
        var voucher = CreatePlatformVoucher();
        _vouchers.GetByIdAsync(voucher.Id, Arg.Any<CancellationToken>()).Returns(voucher);

        VoucherUsage? capturedUsage = null;
        _vouchers.AddUsageAsync(
            Arg.Do<VoucherUsage>(u => capturedUsage = u),
            Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sut = BuildSut();
        var usageId = await sut.RecordUsageAsync(
            voucherId: voucher.Id,
            userId: UserId,
            bookingId: BookingId,
            bookingGroupId: null,
            discountAmount: Money.FromRaw(20_000));

        usageId.Should().NotBe(Guid.Empty);
        capturedUsage.Should().NotBeNull();
        capturedUsage!.BookingId.Should().Be(BookingId);
        capturedUsage.UserId.Should().Be(UserId);
        capturedUsage.VoucherId.Should().Be(voucher.Id);
        capturedUsage.DiscountAmount.Amount.Should().Be(20_000);
        capturedUsage.FundedBy.Should().Be(VoucherFundingType.VIETRIDE_FUNDED);
    }

    // -----------------------------------------------------------------------
    // CompensateAsync — physically deletes usage row
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CompensateAsync_CallsDeleteUsageByBooking()
    {
        _vouchers.DeleteUsageByBookingAsync(BookingId, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sut = BuildSut();
        await sut.CompensateAsync(BookingId);

        await _vouchers.Received(1)
            .DeleteUsageByBookingAsync(BookingId, Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Q8 invariant: route-scope check applies even for operator-owned vouchers (branch a)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidateAndComputeDiscount_OperatorOwned_WrongRoute_ThrowsNotApplicable()
    {
        var voucher = Voucher.Create(
            code: "OPVCH001",
            name: "Operator Route Voucher",
            type: VoucherType.FIXED_AMOUNT,
            value: 10_000,
            minOrderAmount: Money.FromRaw(0),
            maxDiscountAmount: null,
            totalUsageLimit: null,
            perUserLimit: null,
            validFrom: Now.AddDays(-1),
            validUntil: Now.AddDays(30),
            applicableOperatorIds: [OperatorId],
            applicableRouteIds: [RouteId], // restricted to one route
            fundingType: VoucherFundingType.OPERATOR_FUNDED,
            ownerOperatorId: OperatorId,
            createdByUserId: Guid.NewGuid());

        _vouchers.FindByCodeAsync("OPVCH001", Arg.Any<CancellationToken>()).Returns(voucher);

        var sut = BuildSut();
        // Same operator (branch a) but different route
        var act = () => sut.ValidateAndComputeDiscountAsync(
            "OPVCH001", OperatorId, OtherRouteId, UserId,
            orderAmount: Money.FromRaw(200_000),
            now: Now);

        var ex = await act.Should().ThrowAsync<CodedValidationException>();
        ex.Which.ErrorCode.Should().Be("VOUCHER_NOT_APPLICABLE",
            "route-scope check applies even for operator-owned vouchers (Q8)");
    }
}
