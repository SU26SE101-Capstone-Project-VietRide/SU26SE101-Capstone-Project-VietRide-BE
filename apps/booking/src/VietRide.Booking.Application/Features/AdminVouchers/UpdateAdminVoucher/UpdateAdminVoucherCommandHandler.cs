using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Application.Features.AdminVouchers.UpdateAdminVoucher;

/// <summary>
/// Handles PATCH /v1/admin/vouchers/{id} — partial update for platform-owned vouchers only.
/// </summary>
public sealed class UpdateAdminVoucherCommandHandler
    : IRequestHandler<UpdateAdminVoucherCommand, UpdateAdminVoucherResult>
{
    private readonly IVoucherRepository _vouchers;
    private readonly ILogger<UpdateAdminVoucherCommandHandler> _logger;

    public UpdateAdminVoucherCommandHandler(
        IVoucherRepository vouchers,
        ILogger<UpdateAdminVoucherCommandHandler> logger)
    {
        _vouchers = vouchers;
        _logger = logger;
    }

    public async Task<UpdateAdminVoucherResult> Handle(
        UpdateAdminVoucherCommand request,
        CancellationToken cancellationToken)
    {
        var voucher = await _vouchers.FindPlatformByIdAsync(request.VoucherId, cancellationToken);
        if (voucher is null)
        {
            throw new CodedNotFoundException(
                "VOUCHER_NOT_FOUND",
                $"Voucher '{request.VoucherId}' not found.");
        }

        var usageCount = await _vouchers.CountUsagesAsync(voucher.Id, cancellationToken);
        var isLocked = usageCount >= 1;

        if (isLocked)
        {
            const string LockedMessage =
                "Voucher economic fields (value, minOrderAmount, maxDiscountAmount) cannot be changed after the voucher has been used.";
            const string LockedDateMessage =
                "validFrom is frozen once a voucher has been used. validUntil may only be extended, not shortened.";
            const string LockedLimitMessage =
                "Usage limits may only be loosened once a voucher has been used.";

            if (request.Value.HasValue && request.Value.Value != voucher.Value)
                throw new CodedConflictException("VOUCHER_LOCKED", LockedMessage);

            if (request.MinOrderAmount.HasValue && request.MinOrderAmount.Value != voucher.MinOrderAmount.Amount)
                throw new CodedConflictException("VOUCHER_LOCKED", LockedMessage);

            if (request.MaxDiscountAmount.HasValue && request.MaxDiscountAmount.Value != voucher.MaxDiscountAmount?.Amount)
                throw new CodedConflictException("VOUCHER_LOCKED", LockedMessage);

            if (request.ValidFrom.HasValue && request.ValidFrom.Value != voucher.ValidFrom)
                throw new CodedConflictException("VOUCHER_LOCKED", LockedDateMessage);

            if (request.ValidUntil.HasValue && request.ValidUntil.Value < voucher.ValidUntil)
                throw new CodedConflictException("VOUCHER_LOCKED", LockedDateMessage);

            if (IsTightening(voucher.TotalUsageLimit, request.TotalUsageLimit))
                throw new CodedConflictException("VOUCHER_LOCKED", LockedLimitMessage);

            if (IsTightening(voucher.PerUserLimit, request.PerUserLimit))
                throw new CodedConflictException("VOUCHER_LOCKED", LockedLimitMessage);
        }

        var effectiveValue = request.Value ?? voucher.Value;
        var effectiveMinOrder = request.MinOrderAmount.HasValue
            ? Money.FromRaw(request.MinOrderAmount.Value)
            : voucher.MinOrderAmount;
        var effectiveMaxDiscount = request.MaxDiscountAmount.HasValue
            ? Money.FromRaw(request.MaxDiscountAmount.Value)
            : voucher.MaxDiscountAmount;

        var effectiveName = request.Name ?? voucher.Name;
        var effectiveValidFrom = request.ValidFrom ?? voucher.ValidFrom;
        var effectiveValidUntil = request.ValidUntil ?? voucher.ValidUntil;
        var effectiveTotalUsageLimit = request.TotalUsageLimit ?? voucher.TotalUsageLimit;
        var effectivePerUserLimit = request.PerUserLimit ?? voucher.PerUserLimit;
        var effectiveNewUserOnly = request.NewUserOnly ?? voucher.NewUserOnly;
        IReadOnlyCollection<string> effectivePaymentMethods =
            request.ApplicablePaymentMethods is null
                ? voucher.ApplicablePaymentMethods
                : request.ApplicablePaymentMethods;
        IReadOnlyCollection<string> effectiveServices =
            request.ApplicableServices is null
                ? voucher.ApplicableServices
                : request.ApplicableServices;
        IReadOnlyCollection<Guid> effectiveRouteIds =
            request.ApplicableRouteIds is null
                ? voucher.ApplicableRouteIds
                : request.ApplicableRouteIds;

        voucher.UpdateMutableFields(
            name: effectiveName,
            value: effectiveValue,
            minOrderAmount: effectiveMinOrder,
            maxDiscountAmount: effectiveMaxDiscount,
            totalUsageLimit: effectiveTotalUsageLimit,
            perUserLimit: effectivePerUserLimit,
            validFrom: effectiveValidFrom,
            validUntil: effectiveValidUntil,
            newUserOnly: effectiveNewUserOnly,
            applicablePaymentMethods: effectivePaymentMethods,
            applicableServices: effectiveServices,
            applicableRouteIds: effectiveRouteIds);

        _vouchers.Update(voucher);

        _logger.LogInformation(
            "Admin platform voucher {VoucherId} updated (locked={IsLocked}).",
            voucher.Id,
            isLocked);

        return new UpdateAdminVoucherResult(
            Id: voucher.Id,
            Code: voucher.Code,
            Name: voucher.Name,
            Type: voucher.Type.ToString(),
            Value: voucher.Value,
            FundingType: voucher.FundingType.ToString(),
            OwnerOperatorId: voucher.OwnerOperatorId,
            IsActive: voucher.IsActive,
            ValidFrom: voucher.ValidFrom,
            ValidUntil: voucher.ValidUntil,
            NewUserOnly: voucher.NewUserOnly,
            ApplicablePaymentMethods: voucher.ApplicablePaymentMethods,
            ApplicableServices: voucher.ApplicableServices,
            ApplicableRouteIds: voucher.ApplicableRouteIds);
    }

    private static bool IsTightening(int? current, int? requested)
    {
        if (!current.HasValue && requested.HasValue)
            return true;

        if (current.HasValue && requested.HasValue && requested.Value < current.Value)
            return true;

        return false;
    }
}
