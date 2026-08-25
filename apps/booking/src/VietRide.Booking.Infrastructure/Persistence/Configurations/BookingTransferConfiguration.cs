using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Booking.Domain.Entities;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.Infrastructure.Persistence.Configurations;

internal sealed class BookingTransferConfiguration : IEntityTypeConfiguration<BookingTransfer>
{
    public void Configure(EntityTypeBuilder<BookingTransfer> builder)
    {
        builder.ToTable("booking_transfers");

        builder.HasKey(transfer => transfer.Id);

        builder.Property(transfer => transfer.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");
        builder.Property(transfer => transfer.BookingId)
            .HasColumnName("booking_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(transfer => transfer.PassengerId)
            .HasColumnName("passenger_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(transfer => transfer.TicketId)
            .HasColumnName("ticket_id")
            .HasColumnType("uuid")
            .IsRequired(false);
        builder.Property(transfer => transfer.OriginalTripId)
            .HasColumnName("original_trip_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(transfer => transfer.NewTripId)
            .HasColumnName("new_trip_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(transfer => transfer.OriginalSeatNumber)
            .HasColumnName("original_seat_number")
            .HasMaxLength(20)
            .IsRequired(false);
        builder.Property(transfer => transfer.NewSeatNumber)
            .HasColumnName("new_seat_number")
            .HasMaxLength(20)
            .IsRequired(false);
        builder.Property(transfer => transfer.OriginalSeatType)
            .HasColumnName("original_seat_type")
            .HasMaxLength(30)
            .IsRequired(false);
        builder.Property(transfer => transfer.NewSeatType)
            .HasColumnName("new_seat_type")
            .HasMaxLength(30)
            .IsRequired(false);
        builder.Property(transfer => transfer.IsSeatDowngrade)
            .HasColumnName("is_seat_downgrade")
            .HasDefaultValue(false)
            .IsRequired();
        builder.Property(transfer => transfer.ConfirmationStatus)
            .HasColumnName("confirmation_status")
            .HasColumnType("vietride_booking.booking_transfer_confirmation_status")
            .IsRequired();
        builder.Property(transfer => transfer.ConfirmedAt)
            .HasColumnName("confirmed_at")
            .IsRequired(false);
        builder.Property(transfer => transfer.ConfirmedByUserId)
            .HasColumnName("confirmed_by_user_id")
            .HasColumnType("uuid")
            .IsRequired(false);
        builder.Property(transfer => transfer.TransferredAt)
            .HasColumnName("transferred_at")
            .HasDefaultValueSql("now()")
            .IsRequired();
        builder.Property(transfer => transfer.TransferredByUserId)
            .HasColumnName("transferred_by_user_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(transfer => transfer.Note)
            .HasColumnName("note")
            .HasColumnType("text")
            .IsRequired(false);
        builder.Property(transfer => transfer.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(transfer => transfer.BookingId)
            .HasDatabaseName("idx_booking_transfers_booking_id");
        builder.HasIndex(transfer => transfer.PassengerId)
            .HasDatabaseName("idx_booking_transfers_passenger_id");
        builder.HasIndex(transfer => transfer.TicketId)
            .HasDatabaseName("idx_booking_transfers_ticket_id");
        builder.HasIndex(transfer => transfer.OriginalTripId)
            .HasDatabaseName("idx_booking_transfers_original_trip_id");
        builder.HasIndex(transfer => transfer.NewTripId)
            .HasDatabaseName("idx_booking_transfers_new_trip_id");
        builder.HasIndex(transfer => transfer.TransferredAt)
            .HasDatabaseName("idx_booking_transfers_pending_confirm_transferred_at")
            .HasFilter("confirmation_status = 'PENDING_CONFIRM'");
        builder.HasIndex(transfer => new
        {
            transfer.PassengerId,
            transfer.OriginalTripId,
            transfer.NewTripId,
        })
            .HasDatabaseName("uq_booking_transfers_passenger_trip_pair")
            .IsUnique();

        builder.HasOne<BookingEntity>()
            .WithMany()
            .HasForeignKey(transfer => transfer.BookingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Passenger>()
            .WithMany()
            .HasForeignKey(transfer => transfer.PassengerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(transfer => transfer.TicketId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
