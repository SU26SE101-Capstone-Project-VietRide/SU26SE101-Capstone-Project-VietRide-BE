using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Booking.Infrastructure;
using VietRide.Booking.IntegrationTests.Messaging;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests.Jobs;

public sealed class Day23ScheduleChangeTimeoutFixture : IAsyncLifetime
{
    private readonly string _databaseName = $"vr_b23_timeout_{Guid.NewGuid():N}";
    private string? _connectionString;
    private NpgsqlDataSource? _dataSource;

    public async Task InitializeAsync()
    {
        _connectionString = Day22EventDatabase.CreateConnectionString(_databaseName);
        await Day22EventDatabase.CreateDatabaseAsync(_connectionString, _databaseName);
        _dataSource = Day22EventDatabase.CreateDataSource(_connectionString);
        await using var db = CreateDb(DateTimeOffset.UtcNow);
        await db.Database.MigrateAsync();
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ReloadTypesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is null)
        {
            return;
        }

        await _dataSource.DisposeAsync();
        await Day22EventDatabase.DropDatabaseAsync(_connectionString!, _databaseName);
    }

    public BookingDbContext CreateDb(DateTimeOffset now)
        => Day22EventDatabase.CreateDbContext(
            _dataSource ?? throw new InvalidOperationException("Fixture is not initialized."),
            now);

    public async Task<(Guid ActionId, Guid BookingId)> SeedAsync(
        BookingPendingActionSeverity severity,
        DateTimeOffset initialDeadline,
        DateTimeOffset? terminalDeadline)
    {
        await using var db = CreateDb(initialDeadline.AddHours(-2));
        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(initialDeadline.AddHours(-3)),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            null,
            Money.FromRaw(100_000),
            Money.Zero,
            Money.FromRaw(100_000),
            tripSnapshotDeparture: initialDeadline.AddHours(6));
        booking.Confirm(initialDeadline.AddHours(-3));
        var metadata = JsonSerializer.Serialize(new
        {
            sourceEventId = Guid.NewGuid(),
            oldDeparture = initialDeadline.AddHours(5),
            newDeparture = initialDeadline.AddHours(6),
            severity = severity.ToString(),
            initialDeadline,
            terminalDeadline,
            refundBasisAmount = 100_000L,
            refundPercent = severity == BookingPendingActionSeverity.MEDIUM ? 50 : 100,
            refundAmount = severity == BookingPendingActionSeverity.MEDIUM ? 50_000L : 100_000L,
        });
        var action = BookingPendingAction.Create(
            booking.Id,
            BookingPendingActionReason.SCHEDULE_CHANGE,
            initialDeadline,
            severity,
            metadata);
        db.Bookings.Add(booking);
        db.BookingPendingActions.Add(action);
        await db.SaveChangesAsync();
        return (action.Id, booking.Id);
    }
}
