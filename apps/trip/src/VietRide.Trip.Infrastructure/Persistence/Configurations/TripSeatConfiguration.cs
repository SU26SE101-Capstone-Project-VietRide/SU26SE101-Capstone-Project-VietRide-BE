using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class TripSeatConfiguration : IEntityTypeConfiguration<TripSeat>
{
    public void Configure(EntityTypeBuilder<TripSeat> builder)
    {
        builder.ToTable("trip_seats", TripDbContext.SchemaName);

        builder.HasKey(seat => seat.Id).HasName("pk_trip_seats");

        builder.Property(seat => seat.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");
        builder.Property(seat => seat.TripId).HasColumnName("trip_id");
        builder.Property(seat => seat.SeatNumber)
            .HasColumnName("seat_number")
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(seat => seat.SeatType)
            .HasColumnName("seat_type")
            .HasColumnType("trip_seat_type")
            .HasDefaultValueSql("'STANDARD'::trip_seat_type");
        builder.Property(seat => seat.Status)
            .HasColumnName("status")
            .HasColumnType("trip_seat_status")
            .HasDefaultValueSql("'AVAILABLE'::trip_seat_status");
        builder.Property(seat => seat.DisabledReason).HasColumnName("disabled_reason");
        builder.Property(seat => seat.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(seat => seat.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Ignore(seat => seat.RowVersion);

        builder.HasIndex(seat => new { seat.TripId, seat.SeatNumber })
            .IsUnique()
            .HasDatabaseName("uq_trip_seats_trip_seat");
        builder.HasIndex(seat => new { seat.TripId, seat.Status })
            .HasDatabaseName("idx_trip_seats_trip_status");
    }
}
