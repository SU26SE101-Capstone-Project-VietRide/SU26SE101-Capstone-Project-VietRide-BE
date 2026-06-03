using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Infrastructure.Persistence.Configurations;

internal sealed class UserDeviceConfiguration : IEntityTypeConfiguration<UserDevice>
{
    public void Configure(EntityTypeBuilder<UserDevice> builder)
    {
        builder.ToTable("user_devices");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(d => d.UserId)
            .HasColumnName("user_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(d => d.FcmToken)
            .HasColumnName("fcm_token")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(d => d.Platform)
            .HasColumnName("platform")
            .HasColumnType("device_platform")
            .IsRequired();

        builder.Property(d => d.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(d => d.LastActiveAt)
            .HasColumnName("last_active_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(d => d.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(d => d.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Ignore(d => d.RowVersion);

        // FK to users (CASCADE).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_user_devices_user_id");

        // Unique: (user_id, fcm_token).
        builder.HasIndex(d => new { d.UserId, d.FcmToken })
            .HasDatabaseName("uq_user_devices_user_fcm_token")
            .IsUnique();

        builder.HasIndex(d => d.FcmToken)
            .HasDatabaseName("idx_user_devices_fcm_token")
            .HasFilter("is_active = TRUE");

        builder.HasIndex(d => d.UserId)
            .HasDatabaseName("idx_user_devices_user_active")
            .HasFilter("is_active = TRUE");

        builder.HasIndex(d => d.LastActiveAt)
            .HasDatabaseName("idx_user_devices_last_active_at")
            .HasFilter("is_active = TRUE");
    }
}
