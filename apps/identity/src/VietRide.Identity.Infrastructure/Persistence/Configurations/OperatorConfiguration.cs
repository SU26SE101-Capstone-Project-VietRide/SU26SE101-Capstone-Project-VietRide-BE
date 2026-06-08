using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Infrastructure.Persistence.Configurations;

internal sealed class OperatorConfiguration : IEntityTypeConfiguration<Operator>
{
    public void Configure(EntityTypeBuilder<Operator> builder)
    {
        builder.ToTable("operators");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(o => o.Name)
            .HasColumnName("name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(o => o.BusinessRegistrationNumber)
            .HasColumnName("business_registration_number")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(o => o.TaxCode)
            .HasColumnName("tax_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(o => o.ContactEmail)
            .HasColumnName("contact_email")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(o => o.ContactPhone)
            .HasColumnName("contact_phone")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(o => o.LogoUrl)
            .HasColumnName("logo_url")
            .IsRequired(false);

        builder.Property(o => o.AddressStreet)
            .HasColumnName("address_street")
            .HasMaxLength(255)
            .IsRequired(false);

        builder.Property(o => o.AddressWard)
            .HasColumnName("address_ward")
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(o => o.AddressDistrict)
            .HasColumnName("address_district")
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(o => o.AddressProvince)
            .HasColumnName("address_province")
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(o => o.RepresentativeName)
            .HasColumnName("representative_name")
            .HasMaxLength(255)
            .IsRequired(false);

        builder.Property(o => o.RepresentativePhone)
            .HasColumnName("representative_phone")
            .HasMaxLength(20)
            .IsRequired(false);

        builder.Property(o => o.RegistrationStatus)
            .HasColumnName("registration_status")
            .HasColumnType("operator_registration_status")
            .HasDefaultValue(OperatorRegistrationStatus.PENDING)
            .IsRequired();

        builder.Property(o => o.ApprovedAt)
            .HasColumnName("approved_at")
            .IsRequired(false);

        builder.Property(o => o.ApprovedByUserId)
            .HasColumnName("approved_by_user_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(o => o.RejectedAt)
            .HasColumnName("rejected_at")
            .IsRequired(false);

        builder.Property(o => o.RejectedByUserId)
            .HasColumnName("rejected_by_user_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(o => o.RejectReason)
            .HasColumnName("reject_reason")
            .IsRequired(false);

        builder.Property(o => o.SuspendedAt)
            .HasColumnName("suspended_at")
            .IsRequired(false);

        builder.Property(o => o.SuspendReason)
            .HasColumnName("suspend_reason")
            .IsRequired(false);

        builder.Property(o => o.CancellationPolicy)
            .HasColumnName("cancellation_policy")
            .HasColumnType("jsonb")
            .HasConversion(
                value => ToJsonElement(value),
                value => ToJsonString(value))
            .IsRequired(false)
            .HasComment("JSONB array of {hoursBeforeDeparture, feePercent}; sorted ascending. NULL = no policy configured.");

        builder.Property(o => o.ParcelNoShowPolicy)
            .HasColumnName("parcel_no_show_policy")
            .HasColumnType("jsonb")
            .HasConversion(
                value => ToJsonElement(value),
                value => ToJsonString(value))
            .IsRequired(false)
            .HasComment("JSONB {noShowFeePercent, additionalPaymentTimeoutMinutes}. NULL defaults to {0, 30}.");

        builder.Property(o => o.LuggagePolicy)
            .HasColumnName("luggage_policy")
            .HasColumnType("jsonb")
            .HasConversion(
                value => ToJsonElement(value),
                value => ToJsonString(value))
            .IsRequired(false)
            .HasComment("JSONB {defaultLuggageKgPerSeat}. NULL defaults to {10}.");

        builder.Property(o => o.BankAccountName)
            .HasColumnName("bank_account_name")
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(o => o.BankAccountNumber)
            .HasColumnName("bank_account_number")
            .HasMaxLength(20)
            .IsRequired(false);

        builder.Property(o => o.BankName)
            .HasColumnName("bank_name")
            .HasMaxLength(200)
            .IsRequired(false);

        builder.Property(o => o.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(o => o.DeletedAt)
            .HasColumnName("deleted_at")
            .IsRequired(false);

        builder.Property(o => o.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(o => o.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        // BaseEntity exposes RowVersion for aggregates that opt into optimistic concurrency.
        // The canonical identity schema has no operators.row_version column, so this ignore is intentional.
        builder.Ignore(o => o.RowVersion);

        builder.HasIndex(o => o.BusinessRegistrationNumber)
            .HasDatabaseName("uq_operators_business_reg_number")
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(o => o.TaxCode)
            .HasDatabaseName("uq_operators_tax_code")
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(o => o.RegistrationStatus)
            .HasDatabaseName("idx_operators_registration_status");

        builder.HasIndex(o => o.IsActive)
            .HasDatabaseName("idx_operators_is_active");

        builder.HasQueryFilter(o => o.DeletedAt == null);
    }

    private static JsonElement? ToJsonElement(string? value)
    {
        if (value is null)
            return null;

        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    private static string? ToJsonString(JsonElement? value)
        => value?.GetRawText();
}
