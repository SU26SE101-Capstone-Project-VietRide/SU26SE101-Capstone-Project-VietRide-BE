using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Domain.Entities;

/// <summary>
/// Bus operator tenant root. Soft-delete is tracked by <see cref="DeletedAt"/> only;
/// <see cref="IsActive"/> is a separate operational activation flag.
/// </summary>
public sealed class Operator : BaseEntity<Guid>, ISoftDeletable, IActivatable
{
    public string Name { get; private set; } = string.Empty;
    public string BusinessRegistrationNumber { get; private set; } = string.Empty;
    public string TaxCode { get; private set; } = string.Empty;
    public string ContactEmail { get; private set; } = string.Empty;
    public string ContactPhone { get; private set; } = string.Empty;
    public string? LogoUrl { get; private set; }
    public string? AddressStreet { get; private set; }
    public string? AddressWard { get; private set; }
    public string? AddressDistrict { get; private set; }
    public string? AddressProvince { get; private set; }
    public string? RepresentativeName { get; private set; }
    public string? RepresentativePhone { get; private set; }
    public OperatorRegistrationStatus RegistrationStatus { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? RejectedAt { get; private set; }
    public Guid? RejectedByUserId { get; private set; }
    public string? RejectReason { get; private set; }
    public DateTimeOffset? SuspendedAt { get; private set; }
    public string? SuspendReason { get; private set; }

    /// <summary>JSONB array of {hoursBeforeDeparture, feePercent}; null means no policy configured.</summary>
    public string? CancellationPolicy { get; private set; }

    /// <summary>JSONB object {noShowFeePercent, additionalPaymentTimeoutMinutes}; null uses default {0, 30}.</summary>
    public string? ParcelNoShowPolicy { get; private set; }

    /// <summary>JSONB object {defaultLuggageKgPerSeat}; null uses default {10}.</summary>
    public string? LuggagePolicy { get; private set; }

    public string? BankAccountName { get; private set; }
    public string? BankAccountNumber { get; private set; }
    public string? BankName { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset? DeletedAt { get; private set; }

    private Operator() { }

    public static Operator CreatePending(
        string name,
        string businessRegistrationNumber,
        string taxCode,
        string contactEmail,
        string contactPhone,
        string? addressStreet = null,
        string? addressWard = null,
        string? addressDistrict = null,
        string? addressProvince = null,
        string? representativeName = null,
        string? representativePhone = null)
    {
        ValidateRequired(name, nameof(name));
        ValidateRequired(businessRegistrationNumber, nameof(businessRegistrationNumber));
        ValidateRequired(taxCode, nameof(taxCode));
        ValidateRequired(contactEmail, nameof(contactEmail));
        ValidateRequired(contactPhone, nameof(contactPhone));

        return new Operator
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            BusinessRegistrationNumber = businessRegistrationNumber.Trim(),
            TaxCode = taxCode.Trim(),
            ContactEmail = contactEmail.Trim().ToLowerInvariant(),
            ContactPhone = contactPhone.Trim(),
            AddressStreet = NormalizeOptional(addressStreet),
            AddressWard = NormalizeOptional(addressWard),
            AddressDistrict = NormalizeOptional(addressDistrict),
            AddressProvince = NormalizeOptional(addressProvince),
            RepresentativeName = NormalizeOptional(representativeName),
            RepresentativePhone = NormalizeOptional(representativePhone),
            RegistrationStatus = OperatorRegistrationStatus.PENDING,
            IsActive = true,
        };
    }

    public static Operator CreateApproved(
        string name,
        string businessRegistrationNumber,
        string taxCode,
        string contactEmail,
        string contactPhone,
        Guid approvedByUserId,
        DateTimeOffset approvedAt)
    {
        var operatorTenant = CreatePending(name, businessRegistrationNumber, taxCode, contactEmail, contactPhone);
        operatorTenant.Approve(approvedByUserId, approvedAt);
        return operatorTenant;
    }

    public void Approve(Guid approvedByUserId, DateTimeOffset approvedAt)
    {
        if (RegistrationStatus != OperatorRegistrationStatus.PENDING)
        {
            throw new InvalidOperationException("Only pending operators can be approved.");
        }

        RegistrationStatus = OperatorRegistrationStatus.APPROVED;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = approvedAt;
        RejectedAt = null;
        RejectedByUserId = null;
        RejectReason = null;
        SuspendedAt = null;
        SuspendReason = null;
        IsActive = true;
    }

    public void Reject(Guid rejectedByUserId, string reason, DateTimeOffset rejectedAt)
    {
        ValidateRequired(reason, nameof(reason));

        if (RegistrationStatus != OperatorRegistrationStatus.PENDING)
        {
            throw new InvalidOperationException("Only pending operators can be rejected.");
        }

        RegistrationStatus = OperatorRegistrationStatus.REJECTED;
        RejectedByUserId = rejectedByUserId;
        RejectedAt = rejectedAt;
        RejectReason = reason.Trim();
        IsActive = false;
    }

    public void Suspend(string reason, DateTimeOffset suspendedAt)
    {
        ValidateRequired(reason, nameof(reason));

        if (RegistrationStatus != OperatorRegistrationStatus.APPROVED)
        {
            throw new InvalidOperationException("Only approved operators can be suspended.");
        }

        RegistrationStatus = OperatorRegistrationStatus.SUSPENDED;
        SuspendedAt = suspendedAt;
        SuspendReason = reason.Trim();
        IsActive = false;
    }

    public void Reactivate()
    {
        if (RegistrationStatus != OperatorRegistrationStatus.SUSPENDED)
        {
            throw new InvalidOperationException("Only suspended operators can be reactivated.");
        }

        RegistrationStatus = OperatorRegistrationStatus.APPROVED;
        IsActive = true;
    }

    public void UpdateProfile(
        string name,
        string contactEmail,
        string contactPhone,
        string? logoUrl,
        string? addressStreet,
        string? addressWard,
        string? addressDistrict,
        string? addressProvince,
        string? representativeName,
        string? representativePhone,
        string? cancellationPolicy,
        string? parcelNoShowPolicy,
        string? luggagePolicy)
    {
        ValidateRequired(name, nameof(name));
        ValidateRequired(contactEmail, nameof(contactEmail));
        ValidateRequired(contactPhone, nameof(contactPhone));

        Name = name.Trim();
        ContactEmail = contactEmail.Trim().ToLowerInvariant();
        ContactPhone = contactPhone.Trim();
        LogoUrl = NormalizeOptional(logoUrl);
        AddressStreet = NormalizeOptional(addressStreet);
        AddressWard = NormalizeOptional(addressWard);
        AddressDistrict = NormalizeOptional(addressDistrict);
        AddressProvince = NormalizeOptional(addressProvince);
        RepresentativeName = NormalizeOptional(representativeName);
        RepresentativePhone = NormalizeOptional(representativePhone);
        CancellationPolicy = NormalizeOptional(cancellationPolicy);
        ParcelNoShowPolicy = NormalizeOptional(parcelNoShowPolicy);
        LuggagePolicy = NormalizeOptional(luggagePolicy);
    }

    public void UpdateBankAccount(string bankAccountName, string bankAccountNumber, string bankName)
    {
        ValidateRequired(bankAccountName, nameof(bankAccountName));
        ValidateRequired(bankAccountNumber, nameof(bankAccountNumber));
        ValidateRequired(bankName, nameof(bankName));

        BankAccountName = bankAccountName.Trim();
        BankAccountNumber = bankAccountNumber.Trim();
        BankName = bankName.Trim();
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public void SoftDelete(DateTimeOffset deletedAt)
    {
        DeletedAt = deletedAt;
        IsActive = false;
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", parameterName);
        }
    }
}
