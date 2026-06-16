using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class TripSeatConfiguration : IEntityTypeConfiguration<TripSeat>
{
    public void Configure(EntityTypeBuilder<TripSeat> builder)
    {
        builder.ToTable("trip_seats");

        builder.HasKey(seat => seat.Id).HasName("pk_trip_seats");
        builder.Ignore(seat => seat.RowVersion);

        builder.Property(seat => seat.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");
        builder.Property(seat => seat.TripId).HasColumnName("trip_id");
        builder.Property(seat => seat.SeatNumber)
            .HasColumnName("seat_number")
            .HasMaxLength(20);
        builder.Property(seat => seat.SeatType)
            .HasColumnName("seat_type")
            .HasColumnType("vietride_trip.trip_seat_type")
            .HasDefaultValue(TripSeatType.STANDARD);
        builder.Property(seat => seat.Status)
            .HasColumnName("status")
            .HasColumnType("vietride_trip.trip_seat_status")
            .HasDefaultValue(TripSeatStatus.AVAILABLE);
        builder.Property(seat => seat.DisabledReason).HasColumnName("disabled_reason");
        builder.Property(seat => seat.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()");
        builder.Property(seat => seat.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()");

        builder.HasIndex(seat => new { seat.TripId, seat.SeatNumber })
            .IsUnique()
            .HasDatabaseName("uq_trip_seats_trip_seat");
        builder.HasIndex(seat => new { seat.TripId, seat.Status }).HasDatabaseName("idx_trip_seats_trip_status");

        builder.HasOne<Domain.Entities.Trip>()
            .WithMany()
            .HasForeignKey(seat => seat.TripId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
