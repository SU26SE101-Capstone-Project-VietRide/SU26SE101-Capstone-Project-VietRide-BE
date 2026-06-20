using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Application.Features.OperatorVouchers.CreateOperatorVoucher;

/// <summary>
/// Handles POST /v1/operator/vouchers — creates an operator-owned OPERATOR_FUNDED voucher.
/// <para>
/// Happy path:
/// <list type="number">
///   <item>Reject if caller sends a fundingType other than OPERATOR_FUNDED (VOUCHER_FORBIDDEN_FUNDING 422).</item>
///   <item>Resolve code (auto-generate if not supplied; check global uniqueness).</item>
///   <item>Create <see cref="Voucher"/> with owner_operator_id = caller operatorId, forced OPERATOR_FUNDED.</item>
///   <item>No consent fan-out (operator-owned vouchers are self-consented).</item>
///   <item>Persist + return result.</item>
/// </list>
/// </para>
/// </summary>
public sealed class CreateOperatorVoucherCommandHandler
    : IRequestHandler<CreateOperatorVoucherCommand, CreateOperatorVoucherResult>
{
    private readonly IVoucherRepository _vouchers;
    private readonly IVoucherCodeGenerator _codeGenerator;
    private readonly IClock _clock;
    private readonly ILogger<CreateOperatorVoucherCommandHandler> _logger;

    public CreateOperatorVoucherCommandHandler(
        IVoucherRepository vouchers,
        IVoucherCodeGenerator codeGenerator,
        IClock clock,
        ILogger<CreateOperatorVoucherCommandHandler> logger)
    {
        _vouchers = vouchers;
        _codeGenerator = codeGenerator;
        _clock = clock;
        _logger = logger;
    }

    public async Task<CreateOperatorVoucherResult> Handle(
        CreateOperatorVoucherCommand request,
        CancellationToken cancellationToken)
    {
        // -----------------------------------------------------------------------
        // 1. Reject non-OPERATOR_FUNDED fundingType if caller supplied one (VOUCHER_FORBIDDEN_FUNDING 422)
        // -----------------------------------------------------------------------
        if (!string.IsNullOrWhiteSpace(request.FundingType)
            && !string.Equals(request.FundingType, "OPERATOR_FUNDED", StringComparison.OrdinalIgnoreCase))
        {
            throw new CodedValidationException(
                "VOUCHER_FORBIDDEN_FUNDING",
                "Operator self-service vouchers must have fundingType OPERATOR_FUNDED.");
        }

        // -----------------------------------------------------------------------
        // 2. Parse type enum (already validated by FluentValidation)
        // -----------------------------------------------------------------------
        var voucherType = Enum.Parse<VoucherType>(request.Type, ignoreCase: true);

        // -----------------------------------------------------------------------
        // 3. Resolve voucher code (manual or auto-generated 8-char base32)
        // -----------------------------------------------------------------------
        string code;
        if (!string.IsNullOrWhiteSpace(request.Code))
        {
            code = request.Code.Trim().ToUpperInvariant();

            var codeConflict = await _vouchers.CodeExistsAsync(code, cancellationToken);
            if (codeConflict)
            {
                throw new ConflictException(
                    "VOUCHER_CODE_CONFLICT",
                    $"A voucher with code '{code}' already exists.");
            }
        }
        else
        {
            code = await GenerateUniqueCodeAsync(cancellationToken);
        }

        // -----------------------------------------------------------------------
        // 4. Create Voucher entity — owner_operator_id = caller, FORCED OPERATOR_FUNDED.
        //    applicableOperatorIds forced to [ownerOperatorId] — self-consented, no fan-out.
        // -----------------------------------------------------------------------
        var createdAt = _clock.UtcNow;

        var minOrderAmount = Money.FromRaw(request.MinOrderAmount);
        var maxDiscountAmount = request.MaxDiscountAmount.HasValue
            ? Money.FromRaw(request.MaxDiscountAmount.Value)
            : (Money?)null;

        var voucher = Voucher.Create(
            code: code,
            name: request.Name,
            type: voucherType,
            value: request.Value,
            minOrderAmount: minOrderAmount,
            maxDiscountAmount: maxDiscountAmount,
            totalUsageLimit: request.TotalUsageLimit,
            perUserLimit: request.PerUserLimit,
            validFrom: request.ValidFrom,
            validUntil: request.ValidUntil,
            applicableOperatorIds: [request.OwnerOperatorId],
            applicableRouteIds: request.ApplicableRouteIds,
            fundingType: VoucherFundingType.OPERATOR_FUNDED,
            ownerOperatorId: request.OwnerOperatorId,
            createdByUserId: request.CreatedByUserId);

        await _vouchers.AddAsync(voucher, cancellationToken);

        _logger.LogInformation(
            "Operator voucher {VoucherId} (code {Code}) created by operator {OperatorId}.",
            voucher.Id,
            voucher.Code,
            request.OwnerOperatorId);

        return new CreateOperatorVoucherResult(
            Id: voucher.Id,
            Code: voucher.Code,
            Name: voucher.Name,
            Type: voucher.Type,
            Value: voucher.Value,
            FundingType: voucher.FundingType,
            OwnerOperatorId: voucher.OwnerOperatorId!.Value,
            IsActive: voucher.IsActive,
            ValidFrom: voucher.ValidFrom,
            ValidUntil: voucher.ValidUntil,
            CreatedAt: createdAt);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken ct)
    {
        const int maxRetries = 5;
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            var candidate = _codeGenerator.Generate();
            var exists = await _vouchers.CodeExistsAsync(candidate, ct);
            if (!exists)
                return candidate;
        }

        throw new InvalidOperationException(
            "Failed to generate a unique voucher code after multiple attempts.");
    }
}
