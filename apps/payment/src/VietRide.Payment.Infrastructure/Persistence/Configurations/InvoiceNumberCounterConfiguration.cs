using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Payment.Domain.Entities;

namespace VietRide.Payment.Infrastructure.Persistence.Configurations;

internal sealed class InvoiceNumberCounterConfiguration : IEntityTypeConfiguration<InvoiceNumberCounter>
{
    public void Configure(EntityTypeBuilder<InvoiceNumberCounter> builder)
    {
        builder.ToTable("invoice_number_counters", table =>
            table.HasCheckConstraint(
                "chk_invoice_number_counters_range",
                "last_value >= 0 AND last_value <= 999999"));
        builder.HasKey(x => x.PeriodKey).HasName("pk_invoice_number_counters");
        builder.Property(x => x.PeriodKey).HasColumnName("period_key").HasColumnType("char(6)");
        builder.Property(x => x.LastValue).HasColumnName("last_value").IsRequired();
    }
}
