using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Domain.Entities;

/// <summary>
/// Voucher aggregate root. A discount instrument applied at booking checkout.
/// <para>
/// Ownership: <see cref="OwnerOperatorId"/> <c>NULL</c> = platform/admin voucher (created by a
/// SYSTEM_ADMIN); non-null = operator self-created voucher scoped to that operator (created by an
/// OPERATOR_ADMIN). Operator-owned vouchers are always <see cref="VoucherFundingType.OPERATOR_FUNDED"/>
/// (enforced by <c>chk_vouchers_operator_owned_funding</c>) and self-consented (no consent fan-out).
/// </para>
/// <para>
/// Soft-delete via <see cref="SoftDelete(DateTimeOffset)"/> sets <see cref="DeletedAt"/>; the
/// <c>code</c> becomes reusable (partial unique index <c>uq_vouchers_code WHERE deleted_at IS NULL</c>).
/// <see cref="IsActive"/> is a SEPARATE activation toggle (ADR 0003) and stays orthogonal to soft-delete.
/// </para>
/// <para>
/// Field-mutation invariants that need DB state (freeze-on-first-use — counting voucher_usages)
/// are enforced in the Application handler, NOT here. This entity enforces only static invariants
/// that match the schema CHECK constraints.
/// </para>
/// </summary>
public sealed class Voucher : BaseEntity<Guid>, ISoftDeletable, IActivatable
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public VoucherType Type { get; private set; }
    /// <summary>Percent (1–100) for <see cref="VoucherType.PERCENT_OFF"/>; VND for <see cref="VoucherType.FIXED_AMOUNT"/>.</summary>
    public long Value { get; private set; }
    public Money MinOrderAmount { get; private set; }
    public Money? MaxDiscountAmount { get; private set; }
    public int? TotalUsageLimit { get; private set; }
    public int? PerUserLimit { get; private set; }
    public DateTimeOffset ValidFrom { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public bool NewUserOnly { get; private set; }
    public List<string> ApplicablePaymentMethods { get; private set; } = [];
    public List<string> ApplicableServices { get; private set; } = ["BOOKING"];

    /// <summary>Logical FK identity.operators. NULL = applies to all operators (admin VIETRIDE_FUNDED only).</summary>
    public List<Guid> ApplicableOperatorIds { get; private set; } = [];

    /// <summary>Logical FK trip.routes. NULL = applies to all routes.</summary>
    public List<Guid> ApplicableRouteIds { get; private set; } = [];

    public VoucherFundingType FundingType { get; private set; }

    /// <summary>Logical FK identity.operators. NULL = platform voucher; non-null = operator self-created.</summary>
    public Guid? OwnerOperatorId { get; private set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>Logical FK identity.users — SYSTEM_ADMIN (platform) or OPERATOR_ADMIN (operator-owned).</summary>
    public Guid CreatedByUserId { get; private set; }

    /// <inheritdoc/>
    public DateTimeOffset? DeletedAt { get; private set; }

    private Voucher() { }

    /// <summary>
    /// Creates a new voucher. Enforces static invariants matching the schema CHECK constraints:
    /// value &gt; 0, valid_until &gt; valid_from, min_order_amount &gt;= 0, and operator-owned ⇒ OPERATOR_FUNDED.
    /// </summary>
    public static Voucher Create(
        string code,
        string name,
        VoucherType type,
        long value,
        Money minOrderAmount,
        Money? maxDiscountAmount,
        int? totalUsageLimit,
        int? perUserLimit,
        DateTimeOffset validFrom,
        DateTimeOffset validUntil,
        IReadOnlyCollection<Guid>? applicableOperatorIds,
        IReadOnlyCollection<Guid>? applicableRouteIds,
        VoucherFundingType fundingType,
        Guid? ownerOperatorId,
        Guid createdByUserId)
        => Create(
            code,
            name,
            type,
            value,
            minOrderAmount,
            maxDiscountAmount,
            totalUsageLimit,
            perUserLimit,
            validFrom,
            validUntil,
            false,
            null,
            ["BOOKING"],
            applicableOperatorIds,
            applicableRouteIds,
            fundingType,
            ownerOperatorId,
            createdByUserId);

    public static Voucher Create(
        string code,
        string name,
        VoucherType type,
        long value,
        Money minOrderAmount,
        Money? maxDiscountAmount,
        int? totalUsageLimit,
        int? perUserLimit,
        DateTimeOffset validFrom,
        DateTimeOffset validUntil,
        bool newUserOnly,
        IReadOnlyCollection<string>? applicablePaymentMethods,
        IReadOnlyCollection<string>? applicableServices,
        IReadOnlyCollection<Guid>? applicableOperatorIds,
        IReadOnlyCollection<Guid>? applicableRouteIds,
        VoucherFundingType fundingType,
        Guid? ownerOperatorId,
        Guid createdByUserId)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Voucher code cannot be null or whitespace.", nameof(code));
        if (code.Length > 50)
            throw new ArgumentException("Voucher code cannot exceed 50 characters.", nameof(code));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Voucher name cannot be null or whitespace.", nameof(name));
        if (name.Length > 120)
            throw new ArgumentException("Voucher name cannot exceed 120 characters.", nameof(name));

        if (value <= 0)
            throw new ArgumentException("Voucher value must be greater than 0.", nameof(value));

        if (minOrderAmount.Amount < 0)
            throw new ArgumentException("Minimum order amount cannot be negative.", nameof(minOrderAmount));

        if (validUntil <= validFrom)
            throw new ArgumentException("valid_until must be greater than valid_from.", nameof(validUntil));

        // chk_vouchers_operator_owned_funding — operator-owned ⇒ OPERATOR_FUNDED.
        if (ownerOperatorId.HasValue && fundingType != VoucherFundingType.OPERATOR_FUNDED)
            throw new ArgumentException("An operator-owned voucher must be OPERATOR_FUNDED.", nameof(fundingType));

        return new Voucher
        {
            Id = Guid.NewGuid(),
            Code = code.Trim(),
            Name = name.Trim(),
            Type = type,
            Value = value,
            MinOrderAmount = minOrderAmount,
            MaxDiscountAmount = maxDiscountAmount,
            TotalUsageLimit = totalUsageLimit,
            PerUserLimit = perUserLimit,
            ValidFrom = validFrom,
            ValidUntil = validUntil,
            NewUserOnly = newUserOnly,
            ApplicablePaymentMethods = applicablePaymentMethods is null
                ? []
                : [.. applicablePaymentMethods.Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0).Distinct()],
            ApplicableServices = applicableServices is null || applicableServices.Count == 0
                ? ["BOOKING"]
                : [.. applicableServices.Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0).Distinct()],
            ApplicableOperatorIds = applicableOperatorIds is null
                ? []
                : [.. applicableOperatorIds],
            ApplicableRouteIds = applicableRouteIds is null
                ? []
                : [.. applicableRouteIds],
            FundingType = fundingType,
            OwnerOperatorId = ownerOperatorId,
            IsActive = true,
            CreatedByUserId = createdByUserId,
        };
    }

    /// <summary>Soft-deletes the voucher (sets <see cref="DeletedAt"/>). Idempotent if already deleted.</summary>
    public void SoftDelete(DateTimeOffset deletedAt)
    {
        if (DeletedAt.HasValue)
            return;
        DeletedAt = deletedAt;
    }

    /// <summary>Activates the voucher (sets <see cref="IsActive"/> = true).</summary>
    public void Activate() => IsActive = true;

    /// <summary>Deactivates the voucher (sets <see cref="IsActive"/> = false).</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>
    /// Updates the operator-mutable fields, enforcing the static invariants
    /// (value &gt; 0, min_order_amount &gt;= 0, valid_until &gt; valid_from, name length).
    /// The freeze-on-first-use guard (which fields are editable once a usage exists) is enforced
    /// by the PATCH handler, which counts voucher_usages before calling this method.
    /// <see cref="Code"/>, <see cref="Type"/>, <see cref="FundingType"/> and
    /// <see cref="OwnerOperatorId"/> are ALWAYS immutable and have no setter here.
    /// </summary>
    public void UpdateMutableFields(
        string name,
        long value,
        Money minOrderAmount,
        Money? maxDiscountAmount,
        int? totalUsageLimit,
        int? perUserLimit,
        DateTimeOffset validFrom,
        DateTimeOffset validUntil,
        IReadOnlyCollection<Guid>? applicableRouteIds)
        => UpdateMutableFields(
            name,
            value,
            minOrderAmount,
            maxDiscountAmount,
            totalUsageLimit,
            perUserLimit,
            validFrom,
            validUntil,
            NewUserOnly,
            ApplicablePaymentMethods,
            ApplicableServices,
            applicableRouteIds);

    public void UpdateMutableFields(
        string name,
        long value,
        Money minOrderAmount,
        Money? maxDiscountAmount,
        int? totalUsageLimit,
        int? perUserLimit,
        DateTimeOffset validFrom,
        DateTimeOffset validUntil,
        bool newUserOnly,
        IReadOnlyCollection<string>? applicablePaymentMethods,
        IReadOnlyCollection<string>? applicableServices,
        IReadOnlyCollection<Guid>? applicableRouteIds)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Voucher name cannot be null or whitespace.", nameof(name));
        if (name.Length > 120)
            throw new ArgumentException("Voucher name cannot exceed 120 characters.", nameof(name));

        if (value <= 0)
            throw new ArgumentException("Voucher value must be greater than 0.", nameof(value));

        if (minOrderAmount.Amount < 0)
            throw new ArgumentException("Minimum order amount cannot be negative.", nameof(minOrderAmount));

        if (validUntil <= validFrom)
            throw new ArgumentException("valid_until must be greater than valid_from.", nameof(validUntil));

        Name = name.Trim();
        Value = value;
        MinOrderAmount = minOrderAmount;
        MaxDiscountAmount = maxDiscountAmount;
        TotalUsageLimit = totalUsageLimit;
        PerUserLimit = perUserLimit;
        ValidFrom = validFrom;
        ValidUntil = validUntil;
        NewUserOnly = newUserOnly;
        ApplicablePaymentMethods = applicablePaymentMethods is null
            ? []
            : [.. applicablePaymentMethods.Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0).Distinct()];
        ApplicableServices = applicableServices is null || applicableServices.Count == 0
            ? ["BOOKING"]
            : [.. applicableServices.Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0).Distinct()];
        ApplicableRouteIds = applicableRouteIds is null
            ? []
            : [.. applicableRouteIds];
    }
}
