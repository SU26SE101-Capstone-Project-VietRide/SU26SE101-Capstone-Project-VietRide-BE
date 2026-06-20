using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Application.Features.Vouchers.CreateVoucher;

/// <summary>
/// Handles POST /v1/admin/vouchers — creates a platform voucher (owner_operator_id always NULL).
/// <para>
/// Happy path:
/// <list type="number">
///   <item>Validate/resolve code (auto-generate if not supplied; check uniqueness).</item>
///   <item>Parse string enums (Type, FundingType) validated by FluentValidation.</item>
///   <item>Create <see cref="Voucher"/> entity (owner_operator_id NULL).</item>
///   <item>For OPERATOR_FUNDED: fan-out one PENDING <see cref="OperatorVoucherConsent"/> per listed operator.</item>
///   <item>Persist + return result.</item>
/// </list>
/// </para>
/// </summary>
public sealed class CreateVoucherCommandHandler
    : IRequestHandler<CreateVoucherCommand, CreateVoucherResult>
{
    private readonly IVoucherRepository _vouchers;
    private readonly IVoucherCodeGenerator _codeGenerator;
    private readonly IClock _clock;
    private readonly ILogger<CreateVoucherCommandHandler> _logger;

    public CreateVoucherCommandHandler(
        IVoucherRepository vouchers,
        IVoucherCodeGenerator codeGenerator,
        IClock clock,
        ILogger<CreateVoucherCommandHandler> logger)
    {
        _vouchers = vouchers;
        _codeGenerator = codeGenerator;
        _clock = clock;
        _logger = logger;
    }

    public async Task<CreateVoucherResult> Handle(
        CreateVoucherCommand request,
        CancellationToken cancellationToken)
    {
        // -----------------------------------------------------------------------
        // 1. Parse string enums (already validated by FluentValidation)
        // -----------------------------------------------------------------------
        var voucherType = Enum.Parse<VoucherType>(request.Type, ignoreCase: true);
        var fundingType = Enum.Parse<VoucherFundingType>(request.FundingType, ignoreCase: true);

        // -----------------------------------------------------------------------
        // 2. Resolve voucher code (manual or auto-generated 8-char base32)
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
            // Auto-generate a unique 8-char base32 code — retry up to 5 times on collision.
            code = await GenerateUniqueCodeAsync(cancellationToken);
        }

        // -----------------------------------------------------------------------
        // 3. Create Voucher entity (owner_operator_id = NULL for admin-created)
        // -----------------------------------------------------------------------
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
            applicableOperatorIds: request.ApplicableOperatorIds,
            applicableRouteIds: request.ApplicableRouteIds,
            fundingType: fundingType,
            ownerOperatorId: null, // always null for admin-created platform vouchers
            createdByUserId: request.CreatedByUserId);

        await _vouchers.AddAsync(voucher, cancellationToken);

        // -----------------------------------------------------------------------
        // 4. OPERATOR_FUNDED fan-out: insert one PENDING consent per targeted operator
        //    (applicableOperatorIds already validated non-null/non-empty by FluentValidation)
        // -----------------------------------------------------------------------
        if (fundingType == VoucherFundingType.OPERATOR_FUNDED
            && request.ApplicableOperatorIds is { Count: > 0 })
        {
            var requestedAt = _clock.UtcNow;
            foreach (var operatorId in request.ApplicableOperatorIds)
            {
                var consent = OperatorVoucherConsent.Create(
                    operatorId: operatorId,
                    voucherId: voucher.Id,
                    requestedAt: requestedAt);

                await _vouchers.AddConsentAsync(consent, cancellationToken);
            }

            _logger.LogInformation(
                "Admin voucher {VoucherId} (code {Code}) created OPERATOR_FUNDED — fanned out {Count} PENDING consent(s).",
                voucher.Id,
                voucher.Code,
                request.ApplicableOperatorIds.Count);
        }
        else
        {
            _logger.LogInformation(
                "Admin voucher {VoucherId} (code {Code}) created {FundingType}.",
                voucher.Id,
                voucher.Code,
                fundingType);
        }

        return new CreateVoucherResult(
            Id: voucher.Id,
            Code: voucher.Code,
            Name: voucher.Name,
            Type: voucher.Type,
            Value: voucher.Value,
            FundingType: voucher.FundingType,
            OwnerOperatorId: voucher.OwnerOperatorId,
            IsActive: voucher.IsActive,
            ValidFrom: voucher.ValidFrom,
            ValidUntil: voucher.ValidUntil,
            CreatedAt: _clock.UtcNow);
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
