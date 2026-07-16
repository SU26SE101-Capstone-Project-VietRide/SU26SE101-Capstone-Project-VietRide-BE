using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class TripStopFareConfiguration : IEntityTypeConfiguration<TripStopFare>
{
    public void Configure(EntityTypeBuilder<TripStopFare> builder)
    {
        builder.ToTable("trip_stop_fares", table =>
        {
            table.HasCheckConstraint("chk_trip_stop_fares_fare_non_negative", "fare_from_this_stop >= 0");
        });

        builder.Ignore(fare => fare.Id);
        builder.Ignore(fare => fare.RowVersion);

        builder.HasKey(fare => new { fare.TripId, fare.StopId }).HasName("pk_trip_stop_fares");

        builder.Property(fare => fare.TripId).HasColumnName("trip_id");
        builder.Property(fare => fare.StopId).HasColumnName("stop_id");
        builder.Property(fare => fare.FareFromThisStop)
            .HasColumnName("fare_from_this_stop")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount));
        builder.Property(fare => fare.Source)
            .HasColumnName("source")
            .HasColumnType("vietride_trip.trip_stop_fare_source")
            .IsRequired();
        builder.Property(fare => fare.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");
        builder.Ignore(fare => fare.UpdatedAt);

        builder.HasOne<Domain.Entities.Trip>()
            .WithMany()
            .HasForeignKey(fare => fare.TripId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Stop>()
            .WithMany()
            .HasForeignKey(fare => fare.StopId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
