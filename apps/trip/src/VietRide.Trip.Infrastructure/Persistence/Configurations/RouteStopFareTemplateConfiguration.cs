using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class RouteStopFareTemplateConfiguration : IEntityTypeConfiguration<RouteStopFareTemplate>
{
    public void Configure(EntityTypeBuilder<RouteStopFareTemplate> builder)
    {
        builder.ToTable("route_stop_fare_templates", table =>
        {
            table.HasCheckConstraint(
                "chk_route_stop_fare_templates_fare_non_negative",
                "fare_from_this_stop >= 0");

            table.HasCheckConstraint(
                "chk_route_stop_fare_templates_effective_order",
                "effective_until IS NULL OR effective_until > effective_from");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.RouteId)
            .HasColumnName("route_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.StopId)
            .HasColumnName("stop_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.FareFromThisStop)
            .HasColumnName("fare_from_this_stop")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .IsRequired();

        builder.Property(x => x.EffectiveFrom)
            .HasColumnName("effective_from")
            .IsRequired();

        builder.Property(x => x.EffectiveUntil)
            .HasColumnName("effective_until")
            .IsRequired(false);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Ignore(x => x.RowVersion);

        builder.HasOne<Route>()
            .WithMany()
            .HasForeignKey(x => x.RouteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Stop>()
            .WithMany()
            .HasForeignKey(x => x.StopId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.RouteId, x.StopId, x.EffectiveFrom })
            .HasDatabaseName("idx_route_stop_fare_templates_route_stop_effective");

        RemoveConventionIndex(builder, nameof(RouteStopFareTemplate.StopId));
    }

    private static void RemoveConventionIndex(EntityTypeBuilder<RouteStopFareTemplate> builder, string propertyName)
    {
        var property = builder.Metadata.FindProperty(propertyName);
        var index = property is null ? null : builder.Metadata.FindIndex(new[] { property });
        if (index is not null)
        {
            builder.Metadata.RemoveIndex(index);
        }
    }
}
