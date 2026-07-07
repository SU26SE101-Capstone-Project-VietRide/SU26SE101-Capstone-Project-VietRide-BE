using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Infrastructure.Persistence.Configurations;

internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.BookingId)
            .HasColumnName("booking_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.PassengerId)
            .HasColumnName("passenger_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.TicketCode)
            .HasColumnName("ticket_code")
            .HasMaxLength(30)
            .HasConversion(code => code.Value, value => TicketCode.Parse(value))
            .IsRequired();

        builder.Property(x => x.SeatNumber)
            .HasColumnName("seat_number")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasColumnType("public.ticket_status")
            .HasDefaultValueSql("'PENDING_PAYMENT'::public.ticket_status")
            .IsRequired();

        builder.Property(x => x.FareAmount)
            .HasColumnName("fare_amount")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .IsRequired();

        builder.Property(x => x.DiscountAmount)
            .HasColumnName("discount_amount")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .HasDefaultValueSql("0")
            .IsRequired();

        builder.Property(x => x.PaidAmount)
            .HasColumnName("paid_amount")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .IsRequired();

        builder.Property(x => x.IssuedAt).HasColumnName("issued_at").IsRequired(false);
        builder.Property(x => x.UsedAt).HasColumnName("used_at").IsRequired(false);
        builder.Property(x => x.CancelledAt).HasColumnName("cancelled_at").IsRequired(false);
        builder.Property(x => x.RefundedAt).HasColumnName("refunded_at").IsRequired(false);
        builder.Property(x => x.ExpiredAt).HasColumnName("expired_at").IsRequired(false);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Ignore(x => x.RowVersion);

        builder.HasIndex(x => x.TicketCode)
            .HasDatabaseName("uq_tickets_ticket_code")
            .IsUnique();

        builder.HasIndex(x => x.PassengerId)
            .HasDatabaseName("uq_tickets_passenger_id")
            .IsUnique();

        builder.HasIndex(x => new { x.BookingId, x.Status })
            .HasDatabaseName("idx_tickets_booking_status");

        builder.HasIndex(x => x.SeatNumber)
            .HasDatabaseName("idx_tickets_seat_number");

        builder.HasOne(x => x.Booking)
            .WithMany(b => b.Tickets)
            .HasForeignKey(x => x.BookingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Passenger)
            .WithOne(p => p.Ticket)
            .HasForeignKey<Ticket>(x => x.PassengerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
