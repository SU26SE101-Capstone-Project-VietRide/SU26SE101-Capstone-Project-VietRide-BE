using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Infrastructure;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.UnitTests.Infrastructure;

public sealed class BookingTransferModelTests
{
    [Fact]
    public void MatchesCanonicalEnumColumnsNullableSeatUniqueTripleAndLogicalForeignKeys()
    {
        using var dataSource = CreateDataSource();
        using var db = CreateDbContext(dataSource);
        var model = db.Model;
        var transfer = model.FindEntityType(typeof(BookingTransfer))
            ?? throw new InvalidOperationException("BookingTransfer is missing from the EF model.");
        var table = StoreObjectIdentifier.Table("booking_transfers", BookingDbContext.SchemaName);

        transfer.GetSchema().Should().Be(BookingDbContext.SchemaName);
        transfer.GetTableName().Should().Be("booking_transfers");
        transfer.GetProperties()
            .Select(property => property.GetColumnName(table))
            .Should()
            .BeEquivalentTo(
                "id",
                "booking_id",
                "passenger_id",
                "ticket_id",
                "original_trip_id",
                "new_trip_id",
                "original_seat_number",
                "new_seat_number",
                "original_seat_type",
                "new_seat_type",
                "is_seat_downgrade",
                "confirmation_status",
                "confirmed_at",
                "confirmed_by_user_id",
                "transferred_at",
                "transferred_by_user_id",
                "note",
                "created_at");
        transfer.FindProperty(nameof(BookingTransfer.OriginalSeatNumber))!.IsNullable.Should().BeTrue();
        transfer.FindProperty(nameof(BookingTransfer.NewSeatNumber))!.IsNullable.Should().BeTrue();
        transfer.FindProperty(nameof(BookingTransfer.OriginalSeatType))!.IsNullable.Should().BeTrue();
        transfer.FindProperty(nameof(BookingTransfer.NewSeatType))!.IsNullable.Should().BeTrue();
        transfer.FindProperty(nameof(BookingTransfer.IsSeatDowngrade))!.IsNullable.Should().BeFalse();
        transfer.FindProperty(nameof(BookingTransfer.ConfirmationStatus))!
            .GetColumnType()
            .Should()
            .Be("vietride_booking.booking_transfer_confirmation_status");

        var unique = transfer.GetIndexes().Single(index =>
            index.GetDatabaseName() == "uq_booking_transfers_passenger_trip_pair");
        unique.IsUnique.Should().BeTrue();
        unique.Properties.Select(property => property.Name).Should().Equal(
            nameof(BookingTransfer.PassengerId),
            nameof(BookingTransfer.OriginalTripId),
            nameof(BookingTransfer.NewTripId));

        transfer.GetForeignKeys()
            .Select(foreignKey => foreignKey.PrincipalEntityType.ClrType)
            .Should()
            .BeEquivalentTo(
                new[] { typeof(VietRide.Booking.Domain.Entities.Booking), typeof(Passenger), typeof(Ticket) });
        transfer.GetForeignKeys()
            .SelectMany(foreignKey => foreignKey.Properties)
            .Select(property => property.Name)
            .Should()
            .BeEquivalentTo(
                nameof(BookingTransfer.BookingId),
                nameof(BookingTransfer.PassengerId),
                nameof(BookingTransfer.TicketId));
        transfer.GetForeignKeys()
            .SelectMany(foreignKey => foreignKey.Properties)
            .Select(property => property.Name)
            .Should()
            .NotContain([nameof(BookingTransfer.OriginalTripId), nameof(BookingTransfer.NewTripId),
                nameof(BookingTransfer.TransferredByUserId), nameof(BookingTransfer.ConfirmedByUserId)]);

        var passenger = model.FindEntityType(typeof(Passenger))
            ?? throw new InvalidOperationException("Passenger is missing from the EF model.");
        passenger.FindProperty(nameof(Passenger.SeatNumber))!.IsNullable.Should().BeTrue();
        passenger.GetIndexes().Single(index =>
                index.GetDatabaseName() == "uq_passengers_booking_seat")
            .IsUnique.Should().BeTrue();

        Enum.GetNames<BookingTransferConfirmationStatus>().Should().Equal(
            "PENDING_CONFIRM",
            "ESCALATED",
            "CONFIRMED",
            "NOT_REQUIRED");
    }

    private static NpgsqlDataSource CreateDataSource()
    {
        var builder = new NpgsqlDataSourceBuilder(
            "Host=localhost;Database=vietride_booking_model;Username=vietride;Password=vietride_dev");
        BookingDbContext.ConfigurePostgresTypes(builder);
        return builder.Build();
    }

    private static BookingDbContext CreateDbContext(NpgsqlDataSource dataSource)
    {
        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseNpgsql(dataSource, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", BookingDbContext.SchemaName))
            .Options;
        return new BookingDbContext(options, new SystemClock());
    }
}
