using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests.Persistence;

public sealed class StationRepositorySearchTests
{
    [Fact]
    public void SearchActiveByName_UsesSqlLevelUnaccentIlikeContains()
    {
        using var dbContext = CreateDbContext("vietride_trip_model_tests");
        var repository = CreateRepository(dbContext);
        var buildQuery = repository.GetType().GetMethod(
            "BuildSearchActiveByNameQuery",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        var query = (IQueryable<Station>)buildQuery.Invoke(repository, ["Mien Tay", null, null, null])!;

        var sql = query.ToQueryString();
        sql.Should().Contain("unaccent(name) ILIKE unaccent('%' || @p0 || '%')");
        sql.Should().Contain("FROM vietride_trip.stations");
    }

    [Fact]
    public async Task SearchActiveByNameAsync_ReturnsAccentInsensitiveMatch_FromPostgres()
    {
        var databaseName = $"vietride_trip_station_search_{Guid.NewGuid():N}";
        await using var dbContext = CreateDbContext(databaseName);
        var repository = CreateRepository(dbContext);

        try
        {
            await dbContext.Database.MigrateAsync();

            var station = Station.Create(
                name: "Bến xe Miền Tây",
                slug: "ben-xe-mien-tay-ho-chi-minh",
                city: "Ho Chi Minh City",
                province: "Ho Chi Minh",
                addressStreet: "Kinh Dương Vương",
                latitude: 10.7212345m,
                longitude: 106.6267890m,
                supportsShuttle: true);
            dbContext.Stations.Add(station);
            await dbContext.SaveChangesAsync();

            var results = await repository.SearchActiveByNameAsync("Mien Tay", null, null, null, CancellationToken.None);

            results.Should().ContainSingle(x => x.Id == station.Id);
            results.Single(x => x.Id == station.Id).Name.Should().Be("Bến xe Miền Tây");
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    private static IStationRepository CreateRepository(TripDbContext dbContext)
    {
        var repositoryType = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Persistence.Repositories.StationRepository",
            throwOnError: true)!;

        return (IStationRepository)Activator.CreateInstance(repositoryType, dbContext)!;
    }

    private static TripDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .UseNpgsql(CreateConnectionString(databaseName))
            .Options;

        return new TripDbContext(options, new SystemClock());
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string defaultConnectionString = "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var connectionString = Environment.GetEnvironmentVariable("VIETRIDE_TRIP_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = defaultConnectionString;
        }

        return connectionString.Contains("{databaseName}", StringComparison.OrdinalIgnoreCase)
            ? connectionString.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase)
            : connectionString;
    }
}
