using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Parcel.Domain.Entities;

namespace VietRide.Parcel.Infrastructure.Persistence.Configurations;

internal sealed class SystemConfigConfiguration : IEntityTypeConfiguration<SystemConfig>
{
    public void Configure(EntityTypeBuilder<SystemConfig> builder)
    {
        builder.ToTable("system_configs", table =>
        {
            table.HasCheckConstraint("chk_system_configs_version_positive", "version > 0");
        });

        builder.HasKey(config => config.Id);
        builder.Ignore(config => config.RowVersion);
        builder.Property(config => config.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(config => config.Key).HasColumnName("key").HasMaxLength(100).IsRequired();
        builder.Property(config => config.DecimalValue).HasColumnName("decimal_value").HasColumnType("decimal(12,4)");
        builder.Property(config => config.Version).HasColumnName("version").IsRequired();
        builder.Property(config => config.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(config => config.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(config => config.EffectiveTo).HasColumnName("effective_to");
        builder.Property(config => config.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(config => config.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        builder.HasIndex(config => new { config.Key, config.Version })
            .IsUnique()
            .HasDatabaseName("uq_system_configs_key_version");
        builder.HasIndex(config => new { config.Key, config.IsActive, config.EffectiveFrom })
            .HasDatabaseName("idx_system_configs_lookup");
    }
}
