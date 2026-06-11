using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests.Persistence;

public sealed class VehicleTypeRepositoryPersistenceTests
{
    [Fact]
    public async Task ListActiveAsync_AppliesSearchSortAndPagingInDatabase()
    {
        var databaseName = $"vietride_trip_vehicle_type_list_{Guid.NewGuid():N}";
        await using var dbContext = CreateDbContext(databaseName);
        var repository = CreateRepository(dbContext);

        try
        {
            await dbContext.Database.MigrateAsync();
            var customType = VehicleType.Create("TASK91_CODE", "Custom type", null, 12);
            var secondCustomType = VehicleType.Create("SECOND_CODE", "Task91 display match", null, 20);
            var inactiveType = VehicleType.Create("TASK91_INACTIVE", "Task 91 inactive", null, 20);
            inactiveType.Deactivate();
            dbContext.VehicleTypes.AddRange(customType, secondCustomType, inactiveType);
            await dbContext.SaveChangesAsync();

            var result = await repository.ListActiveAsync(
                1,
                1,
                "TASK91",
                "code,displayName",
                "code",
                "asc",
                CancellationToken.None);

            result.TotalItems.Should().Be(2);
            result.Items.Should().ContainSingle();
            result.Items[0].Code.Should().Be("SECOND_CODE");
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    private static IVehicleTypeRepository CreateRepository(TripDbContext dbContext)
    {
        var repositoryType = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Persistence.Repositories.VehicleTypeRepository",
            throwOnError: true)!;

        return (IVehicleTypeRepository)Activator.CreateInstance(repositoryType, dbContext)!;
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
        const string defaultConnectionString = "Host=localhost;Port=5432;Database={databaseName};Username=postgres;Password=postgres";
        var connectionString = Environment.GetEnvironmentVariable("VIETRIDE_TRIP_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = defaultConnectionString;

        return connectionString.Contains("{databaseName}", StringComparison.OrdinalIgnoreCase)
            ? connectionString.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase)
            : connectionString;
    }
}
