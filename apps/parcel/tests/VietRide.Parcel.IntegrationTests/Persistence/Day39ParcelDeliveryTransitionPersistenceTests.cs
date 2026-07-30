using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Parcel.Infrastructure;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.IntegrationTests.Persistence;

public sealed class Day39ParcelDeliveryTransitionPersistenceTests
{
    [Fact]
    public async Task ConcurrentUnloadAndDeliver_AllowOneCasWinner_AndPreserveConfirmationFlow()
    {
        var databaseName = $"vietride_parcel_day39_delivery_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            var unloadParcel = CreateParcel("VRP-DAY39-UNLOAD");
            var deliverParcel = CreateParcel("VRP-DAY39-DELIVER");

            await using (var seedContext = CreateDbContext(dataSource))
            {
                await seedContext.Database.MigrateAsync();
                seedContext.Parcels.AddRange(unloadParcel, deliverParcel);
                await seedContext.SaveChangesAsync();
                await SetStatusAsync(seedContext, unloadParcel.Id, ParcelStatus.IN_TRANSIT);
                await SetStatusAsync(seedContext, deliverParcel.Id, ParcelStatus.UNLOADED);
            }

            var unloadTimes = new[]
            {
                new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 15, 8, 0, 1, TimeSpan.Zero),
            };
            var unloadResults = await RunConcurrentAsync(
                dataSource,
                (repository, index) => repository.TryMarkUnloadedAsync(
                    unloadParcel.Id,
                    unloadTimes[index],
                    CancellationToken.None));

            unloadResults.Count(result => result is not null).Should().Be(1);

            await using (var readContext = CreateDbContext(dataSource))
            {
                var persisted = await readContext.Parcels
                    .AsNoTracking()
                    .SingleAsync(parcel => parcel.Id == unloadParcel.Id);
                persisted.Status.Should().Be(ParcelStatus.UNLOADED);
                persisted.UnloadedAt.Should().BeOneOf(unloadTimes);
                persisted.DeliveredPendingConfirmAt.Should().BeNull();
                (await readContext.ParcelDeliveryTokens
                    .AnyAsync(token => token.ParcelId == unloadParcel.Id))
                    .Should()
                    .BeFalse();
            }

            await using (var replayContext = CreateDbContext(dataSource))
            {
                var replay = await CreateRepository(replayContext).TryMarkUnloadedAsync(
                    unloadParcel.Id,
                    unloadTimes[0].AddMinutes(1),
                    CancellationToken.None);
                replay.Should().BeNull();
            }

            var deliveryAttempts = new[]
            {
                new DeliveryAttempt(
                    new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero),
                    new[] { "https://storage.googleapis.com/test/delivery-1.webp" }),
                new DeliveryAttempt(
                    new DateTimeOffset(2026, 7, 15, 9, 0, 1, TimeSpan.Zero),
                    new[] { "https://storage.googleapis.com/test/delivery-2.webp" }),
            };
            var deliveryResults = await RunConcurrentAsync(
                dataSource,
                (repository, index) => repository.TryMarkDeliveredPendingConfirmAsync(
                    deliverParcel.Id,
                    deliveryAttempts[index].PhotoUrls,
                    deliveryAttempts[index].DeliveredAt,
                    CancellationToken.None));

            deliveryResults.Count(result => result is not null).Should().Be(1);

            var rawToken = Guid.NewGuid();
            Guid persistedTokenId;
            await using (var readContext = CreateDbContext(dataSource))
            {
                var persisted = await readContext.Parcels
                    .AsNoTracking()
                    .SingleAsync(parcel => parcel.Id == deliverParcel.Id);
                var winningAttempt = deliveryAttempts.Single(
                    attempt => attempt.DeliveredAt == persisted.DeliveredPendingConfirmAt);

                persisted.Status.Should().Be(ParcelStatus.DELIVERED_PENDING_CONFIRM);
                persisted.DeliveredPendingConfirmAt.Should().Be(winningAttempt.DeliveredAt);
                persisted.DeliveryPhotoUrls.Should().Equal(winningAttempt.PhotoUrls);

                var deliveryToken = ParcelDeliveryToken.Issue(
                    deliverParcel.Id,
                    DeliveryTokenHasher.Hash(rawToken),
                    winningAttempt.DeliveredAt.AddHours(48),
                    Guid.NewGuid(),
                    ParcelDeliveryTokenIssueReason.INITIAL_DELIVERY,
                    winningAttempt.DeliveredAt);
                readContext.ParcelDeliveryTokens.Add(deliveryToken);
                await readContext.SaveChangesAsync();
                persistedTokenId = deliveryToken.Id;
            }

            await using (var confirmationContext = CreateDbContext(dataSource))
            {
                var confirmation = await CreateRepository(confirmationContext).TryConfirmDeliveryAsync(
                    deliverParcel.Id,
                    persistedTokenId,
                    "127.0.0.1",
                    new DateTimeOffset(2026, 7, 15, 9, 30, 0, TimeSpan.Zero),
                    CancellationToken.None);
                confirmation.Should().NotBeNull();
                confirmation!.Status.Should().Be(ParcelStatus.DELIVERY_CONFIRMED);
            }
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    private static async Task<ParcelPaymentTransitionSnapshot?[]> RunConcurrentAsync(
        NpgsqlDataSource dataSource,
        Func<IParcelRepository, int, Task<ParcelPaymentTransitionSnapshot?>> transition)
    {
        await using var firstContext = CreateDbContext(dataSource);
        await using var secondContext = CreateDbContext(dataSource);
        var first = transition(CreateRepository(firstContext), 0);
        var second = transition(CreateRepository(secondContext), 1);
        return await Task.WhenAll(first, second);
    }

    private static ParcelEntity CreateParcel(string parcelCode)
        => ParcelEntity.CreatePendingPayment(
            parcelCode,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Recipient",
            PhoneNumber.Normalize("+84912345678"),
            "recipient@example.com",
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            "Item",
            null,
            ParcelSizeCategory.MEDIUM,
            5m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(100_000));

    private static async Task SetStatusAsync(
        ParcelDbContext dbContext,
        Guid parcelId,
        ParcelStatus status)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE vietride_parcel.parcels
            SET status = CAST({status.ToString()} AS vietride_parcel.parcel_status)
            WHERE id = {parcelId};
            """);
    }

    private static IParcelRepository CreateRepository(ParcelDbContext dbContext)
    {
        var repositoryType = typeof(ParcelDbContext).Assembly.GetType(
            "VietRide.Parcel.Infrastructure.Persistence.Repositories.ParcelRepository",
            throwOnError: true)!;

        return (IParcelRepository)Activator.CreateInstance(repositoryType, dbContext)!;
    }

    private static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        ParcelDbContext.ConfigurePostgresTypes(builder);
        return builder.Build();
    }

    private static ParcelDbContext CreateDbContext(NpgsqlDataSource dataSource)
    {
        var options = new DbContextOptionsBuilder<ParcelDbContext>()
            .UseNpgsql(dataSource, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", ParcelDbContext.SchemaName))
            .Options;

        return new ParcelDbContext(options, new SystemClock());
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string defaultConnectionString =
            "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var configuredConnectionString =
            Environment.GetEnvironmentVariable("VIETRIDE_PARCEL_TEST_CONNECTION_STRING");
        var connectionString = string.IsNullOrWhiteSpace(configuredConnectionString)
            ? defaultConnectionString
            : configuredConnectionString;

        connectionString = connectionString.Replace(
            "{databaseName}",
            databaseName,
            StringComparison.OrdinalIgnoreCase);
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = databaseName,
        };

        return builder.ConnectionString;
    }

    private static async Task CreateDatabaseAsync(string connectionString, string databaseName)
    {
        var adminConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\";", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string connectionString, string databaseName)
    {
        var adminConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record DeliveryAttempt(
        DateTimeOffset DeliveredAt,
        IReadOnlyCollection<string> PhotoUrls);
}
