using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Application.Features.OperatorVouchers.UpdateOperatorVoucher;

/// <summary>
/// Handles PATCH /v1/operator/vouchers/{id} — partial update of operator-owned voucher fields.
/// <para>
/// Freeze-on-first-use guard (Q6 RESOLVED):
/// <list type="bullet">
///   <item>CountUsages == 0: all mutable fields (name, value, minOrderAmount, maxDiscountAmount,
///     totalUsageLimit, perUserLimit, validFrom, validUntil, applicableRouteIds) are editable.</item>
///   <item>CountUsages &gt;= 1 (locked):
///     <list type="bullet">
///       <item>Economic fields (value, minOrderAmount, maxDiscountAmount) are FROZEN →
///         editing them throws 409 VOUCHER_LOCKED.</item>
///       <item>validFrom is FROZEN → attempting to change it throws 409 VOUCHER_LOCKED.</item>
///       <item>validUntil may only be EXTENDED (not shortened below the current value) →
///         shortening throws 409 VOUCHER_LOCKED.</item>
///       <item>totalUsageLimit / perUserLimit may only be LOOSENED (increased, or set to null =
///         unlimited); tightening (including null → finite) throws 409 VOUCHER_LOCKED.</item>
///       <item>name and applicableRouteIds remain freely editable.</item>
///     </list>
///   </item>
/// </list>
/// code, type, fundingType, ownerOperatorId are ALWAYS immutable (not in UpdateOperatorVoucherCommand).
/// Cross-operator access → 404 VOUCHER_NOT_FOUND (tenant isolation).
/// </para>
/// </summary>
public sealed class UpdateOperatorVoucherCommandHandler
    : IRequestHandler<UpdateOperatorVoucherCommand, UpdateOperatorVoucherResult>
{
    private readonly IVoucherRepository _vouchers;
    private readonly ILogger<UpdateOperatorVoucherCommandHandler> _logger;

    public UpdateOperatorVoucherCommandHandler(
        IVoucherRepository vouchers,
        ILogger<UpdateOperatorVoucherCommandHandler> logger)
    {
        _vouchers = vouchers;
        _logger = logger;
    }

    public async Task<UpdateOperatorVoucherResult> Handle(
        UpdateOperatorVoucherCommand request,
        CancellationToken cancellationToken)
    {
        // -----------------------------------------------------------------------
        // 1. Load voucher scoped to caller's operator (tenant isolation)
        // -----------------------------------------------------------------------
        var voucher = await _vouchers.FindByIdAndOwnerAsync(
            request.VoucherId,
            request.CallerOperatorId,
            cancellationToken);

        if (voucher is null)
        {
            throw new CodedNotFoundException(
                "VOUCHER_NOT_FOUND",
                $"Voucher '{request.VoucherId}' not found.");
        }

        // -----------------------------------------------------------------------
        // 2. Freeze-on-first-use guard (Q6)
        // -----------------------------------------------------------------------
        var usageCount = await _vouchers.CountUsagesAsync(voucher.Id, cancellationToken);
        var isLocked = usageCount >= 1;

        if (isLocked)
        {
            const string LockedMessage =
                "Voucher economic fields (value, minOrderAmount, maxDiscountAmount) cannot be changed after the voucher has been used.";
            const string LockedDateMessage =
                "validFrom is frozen once a voucher has been used. validUntil may only be extended, not shortened.";
            const string LockedLimitMessage =
                "Usage limits may only be loosened (increased or set to unlimited) once a voucher has been used.";

            // Economic fields frozen: reject only when the caller explicitly provides a changed value.
            // null = "keep current" → no violation.
            if (request.Value.HasValue && request.Value.Value != voucher.Value)
            {
                throw new CodedConflictException("VOUCHER_LOCKED", LockedMessage);
            }

            if (request.MinOrderAmount.HasValue && request.MinOrderAmount.Value != voucher.MinOrderAmount.Amount)
            {
                throw new CodedConflictException("VOUCHER_LOCKED", LockedMessage);
            }

            if (request.MaxDiscountAmount.HasValue && request.MaxDiscountAmount.Value != voucher.MaxDiscountAmount?.Amount)
            {
                throw new CodedConflictException("VOUCHER_LOCKED", LockedMessage);
            }

            // validFrom is FROZEN once locked: reject only when the caller provides a changed value.
            if (request.ValidFrom.HasValue && request.ValidFrom.Value != voucher.ValidFrom)
            {
                throw new CodedConflictException("VOUCHER_LOCKED", LockedDateMessage);
            }

            // validUntil may only be extended (not shortened) once locked.
            if (request.ValidUntil.HasValue && request.ValidUntil.Value < voucher.ValidUntil)
            {
                throw new CodedConflictException("VOUCHER_LOCKED", LockedDateMessage);
            }

            // Limits may only be loosened once locked.
            // null = unlimited (loosest); going from null → finite is a tightening.
            if (IsTightening(voucher.TotalUsageLimit, request.TotalUsageLimit))
            {
                throw new CodedConflictException("VOUCHER_LOCKED", LockedLimitMessage);
            }

            if (IsTightening(voucher.PerUserLimit, request.PerUserLimit))
            {
                throw new CodedConflictException("VOUCHER_LOCKED", LockedLimitMessage);
            }
        }

        // -----------------------------------------------------------------------
        // 3. Resolve effective values: null = keep current. When locked, frozen fields
        //    silently fall back to current value (freeze guards above already rejected changes).
        // -----------------------------------------------------------------------
        var effectiveValue = request.Value ?? voucher.Value;
        var effectiveMinOrder = request.MinOrderAmount.HasValue
            ? Money.FromRaw(request.MinOrderAmount.Value)
            : voucher.MinOrderAmount;
        var effectiveMaxDiscount = request.MaxDiscountAmount.HasValue
            ? Money.FromRaw(request.MaxDiscountAmount.Value)
            : voucher.MaxDiscountAmount;

        var effectiveValidFrom = request.ValidFrom ?? voucher.ValidFrom;
        var effectiveValidUntil = request.ValidUntil ?? voucher.ValidUntil;
        var effectiveName = request.Name ?? voucher.Name;

        // null = keep current — fall back to current limit so a missing field does NOT
        // silently set to null (= unlimited), loosening a finite cap without an explicit request.
        // The IsTightening guard above intentionally reads the RAW request value (not this resolved
        // fallback), so that null = "keep current" does NOT evaluate as a loosening attempt.
        var effectiveTotalUsageLimit = request.TotalUsageLimit ?? voucher.TotalUsageLimit;
        var effectivePerUserLimit = request.PerUserLimit ?? voucher.PerUserLimit;

        // null = keep current — fall back to the current list so a missing field does
        // NOT silently clear all route restrictions.  IReadOnlyList<Guid> (request) and List<Guid>
        // (voucher) both implement IReadOnlyCollection<Guid>, so the cast is safe.
        var effectiveApplicableRouteIds =
            request.ApplicableRouteIds ?? (IReadOnlyCollection<Guid>?)voucher.ApplicableRouteIds;

        voucher.UpdateMutableFields(
            name: effectiveName,
            value: effectiveValue,
            minOrderAmount: effectiveMinOrder,
            maxDiscountAmount: effectiveMaxDiscount,
            totalUsageLimit: effectiveTotalUsageLimit,
            perUserLimit: effectivePerUserLimit,
            validFrom: effectiveValidFrom,
            validUntil: effectiveValidUntil,
            newUserOnly: voucher.NewUserOnly,
            applicablePaymentMethods: voucher.ApplicablePaymentMethods,
            applicableServices: voucher.ApplicableServices,
            applicableRouteIds: effectiveApplicableRouteIds);

        _vouchers.Update(voucher);

        _logger.LogInformation(
            "Operator voucher {VoucherId} updated by operator {OperatorId} (locked={IsLocked}).",
            voucher.Id,
            request.CallerOperatorId,
            isLocked);

        return new UpdateOperatorVoucherResult(
            Id: voucher.Id,
            Code: voucher.Code,
            Name: voucher.Name,
            Type: voucher.Type.ToString(),
            Value: voucher.Value,
            FundingType: voucher.FundingType.ToString(),
            OwnerOperatorId: voucher.OwnerOperatorId!.Value,
            IsActive: voucher.IsActive,
            ValidFrom: voucher.ValidFrom,
            ValidUntil: voucher.ValidUntil);
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns <c>true</c> if changing a usage-limit field from <paramref name="current"/> to
    /// <paramref name="requested"/> is a TIGHTENING (i.e. the quota becomes more restrictive).
    /// <para>Rules (null = unlimited = loosest):</para>
    /// <list type="bullet">
    ///   <item>null → finite: tightening (introducing a cap where none existed).</item>
    ///   <item>finite → finite where requested &lt; current: tightening.</item>
    ///   <item>finite → null: loosening (allowed).</item>
    ///   <item>finite → finite where requested &gt;= current: loosening (allowed).</item>
    ///   <item>null → null: no change (allowed).</item>
    /// </list>
    /// </summary>
    private static bool IsTightening(int? current, int? requested)
    {
        // null → finite: introducing a cap is a tightening.
        if (!current.HasValue && requested.HasValue)
            return true;

        // finite → finite where requested < current: tightening.
        if (current.HasValue && requested.HasValue && requested.Value < current.Value)
            return true;

        return false;
    }
}
