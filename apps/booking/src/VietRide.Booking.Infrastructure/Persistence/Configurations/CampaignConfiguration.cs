using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Booking.Domain.Entities;

namespace VietRide.Booking.Infrastructure.Persistence.Configurations;

internal sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.ToTable("campaigns", table =>
        {
            table.HasCheckConstraint("chk_campaigns_validity_window", "valid_until > valid_from");
        });

        builder.HasQueryFilter(x => x.DeletedAt == null);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.OwnerOperatorId).HasColumnName("owner_operator_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.ValidFrom).HasColumnName("valid_from").IsRequired();
        builder.Property(x => x.ValidUntil).HasColumnName("valid_until").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at").IsRequired(false);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()").IsRequired();
        builder.Ignore(x => x.RowVersion);

        builder.HasIndex(x => new { x.IsActive, x.ValidUntil }).HasDatabaseName("idx_campaigns_active_validity");
        builder.HasIndex(x => x.OwnerOperatorId).HasDatabaseName("idx_campaigns_owner_operator").HasFilter("owner_operator_id IS NOT NULL AND deleted_at IS NULL");
    }
}
