using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Application.Services;

/// <summary>
/// Validates a voucher against a booking context and records a <see cref="VoucherUsage"/> row.
/// Also handles the compensation (physical DELETE) path.
/// <para>
/// Implements the Q8-canonical checkout validation order (re-plan 2026-06-19):
/// (1) exists + not soft-deleted + is_active;
/// (2) within valid_from..valid_until window;
/// (3) applicability — operator-scope + route-scope + consent gate;
/// (4) min_order_amount met;
/// (5) usage limits (total + per-user);
/// (6) compute discount (PERCENT_OFF rounded half-up AwayFromZero via Money.FromDecimal, no floor-1000).
/// </para>
/// </summary>
public sealed class VoucherService : IVoucherService
{
    private readonly IVoucherRepository _vouchers;
    private readonly IOperatorVoucherConsentRepository _consents;
    private readonly IBookingRepository _bookings;
    private readonly ILogger<VoucherService> _logger;

    public VoucherService(
        IVoucherRepository vouchers,
        IOperatorVoucherConsentRepository consents,
        IBookingRepository bookings,
        ILogger<VoucherService> logger)
    {
        _vouchers = vouchers;
        _consents = consents;
        _bookings = bookings;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<VoucherValidationResult> ValidateAndComputeDiscountAsync(
        string voucherCode,
        Guid operatorId,
        Guid routeId,
        Guid userId,
        Money orderAmount,
        DateTimeOffset now,
        CancellationToken ct = default,
        string service = "BOOKING",
        string? paymentMethod = null)
    {
        // -----------------------------------------------------------------------
        // Step 1: Exists + not soft-deleted (FindByCodeAsync respects HasQueryFilter) + is_active
        // -----------------------------------------------------------------------
        var voucher = await _vouchers.FindByCodeAsync(voucherCode, ct);
        if (voucher is null)
        {
            throw new CodedNotFoundException(
                "VOUCHER_NOT_FOUND",
                $"Voucher '{voucherCode}' not found.");
        }

        // An inactive voucher is indistinguishable from not-found (do not reveal activation state).
        if (!voucher.IsActive)
        {
            throw new CodedNotFoundException(
                "VOUCHER_NOT_FOUND",
                $"Voucher '{voucherCode}' not found.");
        }

        // -----------------------------------------------------------------------
        // Step 2: Validity window
        // -----------------------------------------------------------------------
        if (now < voucher.ValidFrom || now > voucher.ValidUntil)
        {
            throw new CodedValidationException(
                "VOUCHER_EXPIRED",
                $"Voucher '{voucherCode}' is not valid at this time.");
        }

        var normalizedService = service.Trim().ToUpperInvariant();
        if (voucher.ApplicableServices.Count > 0
            && !voucher.ApplicableServices.Contains(normalizedService, StringComparer.OrdinalIgnoreCase))
        {
            throw new CodedValidationException(
                "VOUCHER_NOT_APPLICABLE",
                $"Voucher '{voucherCode}' is not applicable to {normalizedService}.");
        }

        if (!string.IsNullOrWhiteSpace(paymentMethod)
            && voucher.ApplicablePaymentMethods.Count > 0
            && !voucher.ApplicablePaymentMethods.Contains(paymentMethod.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase))
        {
            throw new CodedValidationException(
                "VOUCHER_PAYMENT_METHOD_NOT_APPLICABLE",
                $"Voucher '{voucherCode}' is not applicable to payment method '{paymentMethod}'.");
        }

        if (voucher.NewUserOnly)
        {
            var hasConfirmedBooking = await _bookings.HasConfirmedBookingAsync(userId, ct);
            if (hasConfirmedBooking)
            {
                throw new CodedValidationException(
                    "VOUCHER_NEW_USER_ONLY",
                    $"Voucher '{voucherCode}' is only available for users without confirmed bookings.");
            }
        }

        // -----------------------------------------------------------------------
        // Step 3: Applicability
        //   (a) owner_operator_id == operatorId → skip operator-scope + consent; route-scope still applies
        //   (b) owner_operator_id IS NULL → check operator-scope + consent
        // -----------------------------------------------------------------------
        var isOperatorOwned = voucher.OwnerOperatorId.HasValue;

        if (isOperatorOwned)
        {
            // Branch (a): operator-owned voucher — operator must be the owner
            if (voucher.OwnerOperatorId!.Value != operatorId)
            {
                throw new CodedValidationException(
                    "VOUCHER_NOT_APPLICABLE",
                    $"Voucher '{voucherCode}' is not applicable to this operator.");
            }
            // Consent check bypassed — operator-owned vouchers are self-consented.
        }
        else
        {
            // Branch (b): platform voucher — check operator-scope if an inclusion list is set
            if (voucher.ApplicableOperatorIds.Count > 0
                && !voucher.ApplicableOperatorIds.Contains(operatorId))
            {
                throw new CodedValidationException(
                    "VOUCHER_NOT_APPLICABLE",
                    $"Voucher '{voucherCode}' is not applicable to this operator.");
            }

            // Branch (b): OPERATOR_FUNDED platform voucher requires ACCEPTED consent
            if (voucher.FundingType == VoucherFundingType.OPERATOR_FUNDED)
            {
                var consent = await _consents.FindAcceptedByVoucherAndOperatorAsync(
                    voucher.Id,
                    operatorId,
                    ct);
                if (consent is null)
                {
                    // Do not reveal consent mechanics — same error as not-applicable (v7:4556)
                    throw new CodedValidationException(
                        "VOUCHER_NOT_APPLICABLE",
                        $"Voucher '{voucherCode}' is not applicable to this operator.");
                }
            }
        }

        // Route-scope check applies to ALL branches (Q8: branch (a) only skips operator + consent)
        if (voucher.ApplicableRouteIds.Count > 0
            && !voucher.ApplicableRouteIds.Contains(routeId))
        {
            throw new CodedValidationException(
                "VOUCHER_NOT_APPLICABLE",
                $"Voucher '{voucherCode}' is not applicable to this route.");
        }

        // -----------------------------------------------------------------------
        // Step 4: Minimum order amount
        // -----------------------------------------------------------------------
        if (orderAmount.Amount < voucher.MinOrderAmount.Amount)
        {
            throw new CodedValidationException(
                "VOUCHER_MIN_ORDER_NOT_MET",
                $"Order amount {orderAmount.Amount} VND does not meet the minimum order amount "
                + $"{voucher.MinOrderAmount.Amount} VND for voucher '{voucherCode}'.");
        }

        // -----------------------------------------------------------------------
        // Step 5: Usage limits
        //   5a. Total usage limit
        //   5b. Per-user usage limit
        // -----------------------------------------------------------------------
        if (voucher.TotalUsageLimit.HasValue)
        {
            var totalUsages = await _vouchers.CountUsagesAsync(voucher.Id, ct);
            if (totalUsages >= voucher.TotalUsageLimit.Value)
            {
                throw new CodedValidationException(
                    "VOUCHER_USAGE_LIMIT_REACHED",
                    $"Voucher '{voucherCode}' has reached its total usage limit.");
            }
        }

        if (voucher.PerUserLimit.HasValue)
        {
            var userUsages = await _vouchers.CountUsagesByUserAsync(voucher.Id, userId, ct);
            if (userUsages >= voucher.PerUserLimit.Value)
            {
                throw new CodedValidationException(
                    "VOUCHER_USER_LIMIT_REACHED",
                    $"Voucher '{voucherCode}' has reached the per-user usage limit for this account.");
            }
        }

        // -----------------------------------------------------------------------
        // Step 6: Compute discount
        //   PERCENT_OFF: (value / 100) * orderAmount, capped at max_discount_amount.
        //                Rounded to nearest dong half-up via Money.FromDecimal.
        //                NO floor-1000 (BSOT v1.11.0, BSOT:2079).
        //   FIXED_AMOUNT: min(value, orderAmount).
        // -----------------------------------------------------------------------
        var discount = ComputeDiscount(voucher, orderAmount);

        _logger.LogDebug(
            "Voucher '{VoucherCode}' validated for operator {OperatorId}: discount {Discount} VND.",
            voucherCode,
            operatorId,
            discount.Amount);

        return new VoucherValidationResult(VoucherId: voucher.Id, Discount: discount);
    }

    /// <inheritdoc/>
    public Task<Guid> RecordUsageAsync(
        Guid voucherId,
        Guid userId,
        Guid bookingId,
        Guid? bookingGroupId,
        Money discountAmount,
        CancellationToken ct = default)
        => RecordUsageForReferenceAsync(voucherId, userId, "BOOKING", bookingId, bookingGroupId, discountAmount, ct);

    /// <inheritdoc/>
    public async Task<Guid> RecordUsageForReferenceAsync(
        Guid voucherId,
        Guid userId,
        string? referenceType,
        Guid referenceId,
        Guid? bookingGroupId,
        Money discountAmount,
        CancellationToken ct = default)
    {
        // Fetch the voucher to snapshot FundedBy (funding_type)
        var voucher = await _vouchers.GetByIdAsync(voucherId, ct)
            ?? throw new InvalidOperationException(
                $"Voucher {voucherId} not found when recording usage — validate first.");

        var usage = VoucherUsage.Create(
            voucherId: voucherId,
            userId: userId,
            referenceType: referenceType ?? "BOOKING",
            referenceId: referenceId,
            bookingGroupId: bookingGroupId,
            discountAmount: discountAmount,
            fundedBy: voucher.FundingType);

        await _vouchers.AddUsageAsync(usage, ct);

        _logger.LogInformation(
            "Voucher {VoucherId} usage recorded for {ReferenceType} {ReferenceId}: discount {Discount} VND (usage {UsageId}).",
            voucherId,
            referenceType ?? "BOOKING",
            referenceId,
            discountAmount.Amount,
            usage.Id);

        return usage.Id;
    }

    /// <inheritdoc/>
    public async Task CompensateAsync(Guid bookingId, CancellationToken ct = default)
    {
        await _vouchers.DeleteUsageByBookingAsync(bookingId, ct);

        _logger.LogInformation(
            "Voucher usage for booking {BookingId} physically deleted (compensation).",
            bookingId);
    }

    /// <inheritdoc/>
    public async Task CompensateByReferenceAsync(string referenceType, Guid referenceId, CancellationToken ct = default)
    {
        await _vouchers.DeleteUsageByReferenceAsync(referenceType, referenceId, ct);

        _logger.LogInformation(
            "Voucher usage for {ReferenceType} {ReferenceId} physically deleted (compensation).",
            referenceType,
            referenceId);
    }

    // -----------------------------------------------------------------------
    // Internal helpers
    // -----------------------------------------------------------------------

    /// <summary>Computes the voucher discount amount for the given order. Used internally and in tests.</summary>
    internal static Money ComputeDiscount(Voucher voucher, Money orderAmount)
    {
        long rawDiscount;

        if (voucher.Type == VoucherType.PERCENT_OFF)
        {
            // discount = orderAmount * (value / 100).
            // Money.FromDecimal handles half-up rounding (MidpointRounding.AwayFromZero).
            // No floor-1000 (BSOT v1.11.0, BSOT:2079).
            var percentDecimal = (decimal)voucher.Value / 100m;
            rawDiscount = Money.FromDecimal(orderAmount.Amount * percentDecimal).Amount;
        }
        else
        {
            // FIXED_AMOUNT — capped at order amount
            rawDiscount = Math.Min(voucher.Value, orderAmount.Amount);
        }

        // Apply max_discount_amount cap
        if (voucher.MaxDiscountAmount.HasValue && rawDiscount > voucher.MaxDiscountAmount.Value.Amount)
        {
            rawDiscount = voucher.MaxDiscountAmount.Value.Amount;
        }

        // Discount cannot exceed order amount (safety; already handled for FIXED_AMOUNT above)
        rawDiscount = Math.Min(rawDiscount, orderAmount.Amount);

        return Money.FromRaw(rawDiscount);
    }
}
