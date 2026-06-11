using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.Infrastructure.Persistence.Configurations;

internal sealed class BookingConfiguration : IEntityTypeConfiguration<BookingEntity>
{
    public void Configure(EntityTypeBuilder<BookingEntity> builder)
    {
        builder.ToTable("bookings", table =>
        {
            table.HasCheckConstraint(
                "chk_bookings_pickup_exactly_one",
                "(pickup_station_id IS NOT NULL)::INT + (pickup_stop_id IS NOT NULL)::INT = 1");

            table.HasCheckConstraint(
                "chk_bookings_dropoff_at_most_one",
                "(dropoff_station_id IS NOT NULL)::INT + (dropoff_stop_id IS NOT NULL)::INT <= 1");

            table.HasCheckConstraint(
                "chk_bookings_amounts_non_negative",
                "base_fare >= 0 AND discount_amount >= 0 AND total_amount >= 0");

            table.HasCheckConstraint(
                "chk_bookings_total_le_base",
                "total_amount <= base_fare");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.BookingCode)
            .HasColumnName("booking_code")
            .HasMaxLength(30)
            .HasConversion(
                bc => bc.Value,
                s => VietRide.Booking.Domain.ValueObjects.BookingCode.Parse(s))
            .IsRequired();

        builder.Property(x => x.PassengerUserId)
            .HasColumnName("passenger_user_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.TripId)
            .HasColumnName("trip_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.OperatorId)
            .HasColumnName("operator_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.PickupStationId)
            .HasColumnName("pickup_station_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.PickupStopId)
            .HasColumnName("pickup_stop_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.DropoffStationId)
            .HasColumnName("dropoff_station_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.DropoffStopId)
            .HasColumnName("dropoff_stop_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.BaseFare)
            .HasColumnName("base_fare")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .IsRequired();

        builder.Property(x => x.DiscountAmount)
            .HasColumnName("discount_amount")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .HasDefaultValueSql("0")
            .IsRequired();

        // total_amount is immutable after INSERT — AfterSaveBehavior prevents updates
        builder.Property(x => x.TotalAmount)
            .HasColumnName("total_amount")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .IsRequired()
            .Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasColumnType("booking_status")
            .HasDefaultValue(BookingStatus.PENDING_PAYMENT)
            .IsRequired();

        builder.Property(x => x.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasColumnType("booking_cancellation_reason")
            .IsRequired(false);

        builder.Property(x => x.RefundOverride)
            .HasColumnName("refund_override")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(x => x.BookingGroupId)
            .HasColumnName("booking_group_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.TripDirection)
            .HasColumnName("trip_direction")
            .HasColumnType("trip_direction")
            .IsRequired(false);

        builder.Property(x => x.TripSnapshotOriginName)
            .HasColumnName("trip_snapshot_origin_name")
            .HasMaxLength(255)
            .IsRequired(false);

        builder.Property(x => x.TripSnapshotDestName)
            .HasColumnName("trip_snapshot_dest_name")
            .HasMaxLength(255)
            .IsRequired(false);

        builder.Property(x => x.TripSnapshotDeparture)
            .HasColumnName("trip_snapshot_departure")
            .IsRequired(false);

        builder.Property(x => x.TripSnapshotRouteName)
            .HasColumnName("trip_snapshot_route_name")
            .HasMaxLength(255)
            .IsRequired(false);

        builder.Property(x => x.ConfirmedAt)
            .HasColumnName("confirmed_at")
            .IsRequired(false);

        builder.Property(x => x.CancelledAt)
            .HasColumnName("cancelled_at")
            .IsRequired(false);

        builder.Property(x => x.RefundedAt)
            .HasColumnName("refunded_at")
            .IsRequired(false);

        builder.Property(x => x.ExpiredAt)
            .HasColumnName("expired_at")
            .IsRequired(false);

        builder.Property(x => x.CompletedAt)
            .HasColumnName("completed_at")
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

        builder.HasMany(x => x.Passengers)
            .WithOne(p => p.Booking)
            .HasForeignKey(p => p.BookingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.PendingActions)
            .WithOne(pa => pa.Booking)
            .HasForeignKey(pa => pa.BookingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.BookingCode)
            .HasDatabaseName("uq_bookings_booking_code")
            .IsUnique();

        builder.HasIndex(x => new { x.PassengerUserId, x.CreatedAt })
            .HasDatabaseName("idx_bookings_passenger_user_id_created_at");

        builder.HasIndex(x => new { x.TripId, x.Status })
            .HasDatabaseName("idx_bookings_trip_id_status");

        builder.HasIndex(x => new { x.OperatorId, x.Status })
            .HasDatabaseName("idx_bookings_operator_id_status");

        builder.HasIndex(x => x.BookingGroupId)
            .HasDatabaseName("idx_bookings_booking_group_id")
            .HasFilter("booking_group_id IS NOT NULL");

        builder.HasIndex(x => new { x.Status, x.CreatedAt })
            .HasDatabaseName("idx_bookings_status_created_at")
            .HasFilter("status IN ('PENDING_PAYMENT', 'CONFIRMED')");

        builder.HasIndex(x => x.TripSnapshotDeparture)
            .HasDatabaseName("idx_bookings_trip_snapshot_departure");
    }
}
