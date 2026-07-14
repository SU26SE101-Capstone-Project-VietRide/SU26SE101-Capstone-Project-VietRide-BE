using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Infrastructure.Persistence.Configurations;

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices", table =>
        {
            table.HasCheckConstraint("chk_invoices_amount_non_negative", "amount >= 0");
            table.HasCheckConstraint("chk_invoices_period_order", "period_to > period_from");
            table.HasCheckConstraint(
                "chk_invoices_pdf_attempts",
                "pdf_generation_attempts >= 0 AND pdf_generation_attempts <= 5");
            table.HasCheckConstraint(
                "chk_invoices_issued_consistency",
                "status <> 'ISSUED' OR (issued_at IS NOT NULL AND pdf_url IS NOT NULL AND storage_object_path IS NOT NULL AND pdf_generation_status = 'COMPLETED')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(50).IsRequired();
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.OperatorSubscriptionId).HasColumnName("operator_subscription_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.PaymentId).HasColumnName("payment_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount)).IsRequired();
        builder.Property(x => x.PeriodFrom).HasColumnName("period_from").IsRequired();
        builder.Property(x => x.PeriodTo).HasColumnName("period_to").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status")
            .HasColumnType($"{PaymentDbContext.SchemaName}.invoice_status").HasDefaultValueSql("'DRAFT'").IsRequired();
        builder.Property(x => x.IssuedAt).HasColumnName("issued_at");
        builder.Property(x => x.PdfUrl).HasColumnName("pdf_url").HasColumnType("text");
        builder.Property(x => x.StorageObjectPath).HasColumnName("storage_object_path").HasColumnType("text");
        builder.Property(x => x.PdfGenerationStatus).HasColumnName("pdf_generation_status")
            .HasColumnType($"{PaymentDbContext.SchemaName}.invoice_pdf_generation_status")
            .HasDefaultValueSql("'PENDING'").IsRequired();
        builder.Property(x => x.PdfGenerationAttempts).HasColumnName("pdf_generation_attempts").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.PdfGenerationStartedAt).HasColumnName("pdf_generation_started_at");
        builder.Property(x => x.PdfGenerationNextRetryAt).HasColumnName("pdf_generation_next_retry_at");
        builder.Property(x => x.PdfGenerationLastError).HasColumnName("pdf_generation_last_error").HasColumnType("text");
        builder.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()").IsRequired();
        builder.Ignore(x => x.RowVersion);

        builder.HasIndex(x => x.InvoiceNumber).HasDatabaseName("uq_invoices_invoice_number").IsUnique();
        builder.HasIndex(x => x.PaymentId).HasDatabaseName("uq_invoices_payment_id").IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.CreatedAt }).HasDatabaseName("idx_invoices_operator_id_created_at").IsDescending(false, true);
        builder.HasIndex(x => x.Status).HasDatabaseName("idx_invoices_status");
        builder.HasIndex(x => new { x.PdfGenerationStatus, x.PdfGenerationNextRetryAt })
            .HasDatabaseName("idx_invoices_pdf_retry")
            .HasFilter("pdf_generation_status IN ('PENDING', 'FAILED', 'PROCESSING')");

        builder.HasOne<VietRide.Payment.Domain.Entities.Payment>()
            .WithMany()
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
