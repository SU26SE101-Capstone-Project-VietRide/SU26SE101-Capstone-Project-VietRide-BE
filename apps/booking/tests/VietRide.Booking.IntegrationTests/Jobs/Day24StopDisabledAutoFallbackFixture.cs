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

public sealed class Day24StopDisabledAutoFallbackFixture : IAsyncLifetime
{
    private readonly string _databaseName = $"vr_d24_fallback_{Guid.NewGuid():N}";
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

    public async Task<SeededFallback> SeedAsync(
        DateTimeOffset deadline,
        string affectedField)
    {
        await using var db = CreateDb(deadline.AddHours(-1));
        var disabledStopId = Guid.NewGuid();
        var fallbackStationId = Guid.NewGuid();
        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(deadline.AddHours(-2)),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            affectedField == "PICKUP" ? null : Guid.NewGuid(),
            affectedField == "PICKUP" ? disabledStopId : null,
            null,
            affectedField == "DROPOFF" ? disabledStopId : null,
            Money.FromRaw(100_000),
            Money.Zero,
            Money.FromRaw(100_000));
        booking.Confirm(deadline.AddHours(-2));
        var action = BookingPendingAction.Create(
            booking.Id,
            BookingPendingActionReason.STOP_DISABLED,
            deadline,
            metadata: JsonSerializer.Serialize(new
            {
                disabledStopId,
                affectedField,
                fallbackStationId,
            }));
        db.Bookings.Add(booking);
        db.BookingPendingActions.Add(action);
        await db.SaveChangesAsync();
        return new SeededFallback(
            booking.Id,
            booking.TripId,
            booking.PassengerUserId,
            action.Id,
            disabledStopId,
            fallbackStationId,
            affectedField);
    }
}
