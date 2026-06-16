using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests.Persistence;

public sealed class VehicleRepositoryPersistenceTests
{
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherOperatorId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task ListByOperatorAsync_AppliesTenantSearchSortAndPagingInDatabase()
    {
        var databaseName = $"vietride_trip_vehicle_list_{Guid.NewGuid():N}";
        await using var dbContext = CreateDbContext(databaseName);
        var repository = CreateRepository(dbContext);

        try
        {
            await dbContext.Database.MigrateAsync();
            var vehicleType = await dbContext.VehicleTypes.SingleAsync(x => x.Code == "STANDARD_BUS");
            var ownedActive = CreateVehicle(OperatorId, vehicleType.Id, "51A-20000");
            var ownedOther = CreateVehicle(OperatorId, vehicleType.Id, "51A-10000");
            var crossTenant = CreateVehicle(OtherOperatorId, vehicleType.Id, "51A-30000");
            (await repository.TryAddAsync(ownedActive, CancellationToken.None)).Should().BeTrue();
            (await repository.TryAddAsync(ownedOther, CancellationToken.None)).Should().BeTrue();
            (await repository.TryAddAsync(crossTenant, CancellationToken.None)).Should().BeTrue();

            var result = await repository.ListByOperatorAsync(
                OperatorId,
                1,
                1,
                "51A",
                "licensePlate",
                "licensePlate",
                "desc",
                CancellationToken.None);

            result.TotalItems.Should().Be(2);
            result.Items.Should().ContainSingle();
            result.Items[0].LicensePlate.Should().Be("51A-20000");
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task LicensePlateChecks_AreSoftDeleteAware_AndRaceSafe()
    {
        var databaseName = $"vietride_trip_vehicle_plate_{Guid.NewGuid():N}";
        await using var dbContext = CreateDbContext(databaseName);
        var repository = CreateRepository(dbContext);

        try
        {
            await dbContext.Database.MigrateAsync();
            var vehicleType = await dbContext.VehicleTypes.SingleAsync(x => x.Code == "STANDARD_BUS");

            var firstVehicle = CreateVehicle(OperatorId, vehicleType.Id, "51A-12345");
            (await repository.TryAddAsync(firstVehicle, CancellationToken.None)).Should().BeTrue();

            var racingVehicle = CreateVehicle(OtherOperatorId, vehicleType.Id, "51A-12345");
            (await repository.TryAddAsync(racingVehicle, CancellationToken.None)).Should().BeFalse();

            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE vietride_trip.vehicles SET deleted_at = {DateTimeOffset.UtcNow} WHERE id = {firstVehicle.Id}");

            (await repository.LicensePlateExistsAsync("51A-12345", null, CancellationToken.None))
                .Should()
                .BeFalse();
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    private static Vehicle CreateVehicle(Guid operatorId, Guid vehicleTypeId, string licensePlate)
        => Vehicle.Create(
            operatorId,
            vehicleTypeId,
            licensePlate,
            JsonSerializer.SerializeToElement(new
            {
                version = 1,
                vehicleTypeCode = "STANDARD_BUS",
                totalSeats = 2,
                rows = 1,
                cols = 2,
                decks = 1,
                aisles = Array.Empty<object>(),
                seats = new[]
                {
                    new { seatNumber = "A01", row = 1, col = 1, deck = 1, type = "STANDARD", isWindow = true, isAisle = false, disabled = false },
                    new { seatNumber = "A02", row = 1, col = 2, deck = 1, type = "STANDARD", isWindow = true, isAisle = false, disabled = false },
                },
            }),
            2,
            null,
            null);

    private static IVehicleRepository CreateRepository(TripDbContext dbContext)
    {
        var repositoryType = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Persistence.Repositories.VehicleRepository",
            throwOnError: true)!;

        return (IVehicleRepository)Activator.CreateInstance(repositoryType, dbContext)!;
    }

    private static TripDbContext CreateDbContext(string databaseName)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(CreateConnectionString(databaseName));
        dataSourceBuilder.EnableUnmappedTypes();
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .UseNpgsql(dataSourceBuilder.Build())
            .Options;

        return new TripDbContext(options, new SystemClock());
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string defaultConnectionString = "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var connectionString = Environment.GetEnvironmentVariable("VIETRIDE_TRIP_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = defaultConnectionString;

        return connectionString.Contains("{databaseName}", StringComparison.OrdinalIgnoreCase)
            ? connectionString.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase)
            : connectionString;
    }
}
