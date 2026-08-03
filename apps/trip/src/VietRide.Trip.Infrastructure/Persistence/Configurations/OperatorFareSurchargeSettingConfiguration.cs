using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class OperatorFareSurchargeSettingConfiguration : IEntityTypeConfiguration<OperatorFareSurchargeSetting>
{
    public void Configure(EntityTypeBuilder<OperatorFareSurchargeSetting> builder)
    {
        builder.ToTable("operator_fare_surcharge_settings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("operator_id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();
        builder.Ignore(x => x.OperatorId);
        builder.Property(x => x.IsEnabled)
            .HasColumnName("is_enabled")
            .HasDefaultValue(false)
            .IsRequired();
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();
        builder.Ignore(x => x.RowVersion);
    }
}
