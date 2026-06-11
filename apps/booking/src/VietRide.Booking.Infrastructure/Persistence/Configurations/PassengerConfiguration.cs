using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.Infrastructure.Persistence.Configurations;

internal sealed class PassengerConfiguration : IEntityTypeConfiguration<Passenger>
{
    public void Configure(EntityTypeBuilder<Passenger> builder)
    {
        builder.ToTable("passengers");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.BookingId)
            .HasColumnName("booking_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.SeatNumber)
            .HasColumnName("seat_number")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.BoardingStatus)
            .HasColumnName("boarding_status")
            .HasColumnType("passenger_boarding_status")
            .HasConversion(s => s.ToString(), s => Enum.Parse<PassengerBoardingStatus>(s))
            .HasDefaultValueSql("'PENDING'")
            .IsRequired();

        builder.Property(x => x.BoardedAt)
            .HasColumnName("boarded_at")
            .IsRequired(false);

        builder.Property(x => x.BoardedAtStopId)
            .HasColumnName("boarded_at_stop_id")
            .HasColumnType("uuid")
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

        builder.HasIndex(x => new { x.BookingId, x.SeatNumber })
            .HasDatabaseName("uq_passengers_booking_seat")
            .IsUnique();

        builder.HasIndex(x => new { x.BookingId, x.BoardingStatus })
            .HasDatabaseName("idx_passengers_boarding_status");

        // Relationship is defined in BookingConfiguration — redeclare FK here for completeness.
        builder.HasOne(x => x.Booking)
            .WithMany(b => b.Passengers)
            .HasForeignKey(x => x.BookingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
