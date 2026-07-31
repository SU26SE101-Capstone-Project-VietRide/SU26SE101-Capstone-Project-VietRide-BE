using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Payment.Domain.Entities;

namespace VietRide.Payment.Infrastructure.Persistence.Configurations;

internal sealed class DeletedFinancialActorMarkerConfiguration
    : IEntityTypeConfiguration<DeletedFinancialActorMarker>
{
    public void Configure(EntityTypeBuilder<DeletedFinancialActorMarker> builder)
    {
        builder.ToTable("deleted_financial_actor_markers");
        builder.HasKey(item => item.UserId);
        builder.Property(item => item.UserId)
            .HasColumnName("user_id")
            .HasColumnType("uuid");
        builder.Property(item => item.DeletedAt)
            .HasColumnName("deleted_at")
            .HasDefaultValueSql("now()")
            .IsRequired();
    }
}
