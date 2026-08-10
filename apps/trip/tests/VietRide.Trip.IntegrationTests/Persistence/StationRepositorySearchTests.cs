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
                ward: "An Lac",
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

    [Fact]
    public async Task CatalogStationAndStopSearch_ReturnAccentInsensitiveMatches_FromPostgres()
    {
        var databaseName = $"vietride_trip_accent_search_{Guid.NewGuid():N}";
        await using var dbContext = CreateDbContext(databaseName);
        var locations = CreateLocationRepository(dbContext);
        var stations = CreateRepository(dbContext);
        var stops = CreateStopRepository(dbContext);

        try
        {
            await dbContext.Database.MigrateAsync();

            var hcm = await locations.GetActiveByCodeAsync("79", CancellationToken.None);
            var vungTau = await locations.GetActiveByCodeAsync("26506", CancellationToken.None);
            hcm.Should().NotBeNull();
            vungTau.Should().NotBeNull();

            var station = Station.Create(
                "Bến xe Vũng Tàu",
                $"ben-xe-vung-tau-{Guid.NewGuid():N}",
                hcm!.Name,
                vungTau!.Name,
                locationId: vungTau.Id);
            var stop = Stop.Create(
                Guid.NewGuid(),
                "Bến xe dọc tuyến",
                10.40m,
                107.12m,
                address: "Phường Vũng Tàu, Thành phố Hồ Chí Minh",
                locationId: vungTau.Id);
            dbContext.Stations.Add(station);
            dbContext.Stops.Add(stop);
            await dbContext.SaveChangesAsync();

            var roots = await locations.ListActiveTopLevelAsync("ho chi minh", CancellationToken.None);
            roots.Should().ContainSingle(location => location.Id == hcm.Id);
            var children = await locations.ListActiveChildrenAsync(hcm.Id, "vung tau", CancellationToken.None);
            children.Should().ContainSingle(location => location.Id == vungTau.Id);
            var adminLocations = await locations.ListAsync(1, 20, "vung tau", true, CancellationToken.None);
            adminLocations.Items.Should().ContainSingle(location => location.Id == vungTau.Id);

            var stationNameMatches = await stations.SearchByTextNoTracking("ben xe", false).ToListAsync();
            stationNameMatches.Should().ContainSingle(item => item.Id == station.Id);
            var stationLocationMatches = await stations.SearchByTextNoTracking("vung tau", true).ToListAsync();
            stationLocationMatches.Should().ContainSingle(item => item.Id == station.Id);
            var stopNameMatches = await stops.SearchByTextNoTracking("ben xe").ToListAsync();
            stopNameMatches.Should().ContainSingle(item => item.Id == stop.Id);
            var stopAddressMatches = await stops.SearchByTextNoTracking("ho chi minh").ToListAsync();
            stopAddressMatches.Should().ContainSingle(item => item.Id == stop.Id);
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

    private static ILocationRepository CreateLocationRepository(TripDbContext dbContext)
    {
        var repositoryType = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Persistence.Repositories.LocationRepository",
            throwOnError: true)!;

        return (ILocationRepository)Activator.CreateInstance(repositoryType, dbContext)!;
    }

    private static IStopRepository CreateStopRepository(TripDbContext dbContext)
    {
        var repositoryType = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Persistence.Repositories.StopRepository",
            throwOnError: true)!;

        return (IStopRepository)Activator.CreateInstance(repositoryType, dbContext)!;
    }

    private static TripDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .UseNpgsql(CreateConnectionString(databaseName))
            .ConfigureWarnings(warnings => warnings.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        return new TripDbContext(options, new SystemClock());
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string defaultConnectionString = "Host=127.0.0.1;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
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
