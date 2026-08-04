using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests.Persistence;

public sealed class RouteStopRepositoryPersistenceTests
{
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task Remove_HardDeletesRouteStopJunctionRow()
    {
        var databaseName = $"vietride_trip_route_stop_delete_{Guid.NewGuid():N}";
        await using var dbContext = CreateDbContext(databaseName);
        var repository = CreateRepository(dbContext);

        try
        {
            await dbContext.Database.MigrateAsync();
            var origin = Station.Create("Origin", $"origin-{Guid.NewGuid():N}", "Da Nang", "Da Nang");
            var destination = Station.Create("Destination", $"destination-{Guid.NewGuid():N}", "Hue", "Thua Thien Hue");
            var stop = Stop.Create(OperatorId, "Hai Van", 16.1m, 108.2m);
            var route = Route.Create(OperatorId, "Da Nang to Hue", origin.Id, destination.Id, Money.FromRaw(250000), 100m, 180);
            var routeStop = RouteStop.Create(route.Id, stop.Id, 1, 45, 25m, true, false);
            dbContext.Stations.AddRange(origin, destination);
            dbContext.Stops.Add(stop);
            dbContext.Routes.Add(route);
            dbContext.RouteStops.Add(routeStop);
            await dbContext.SaveChangesAsync();

            repository.Remove(routeStop);
            await dbContext.SaveChangesAsync();

            var exists = await dbContext.RouteStops.AnyAsync(x => x.RouteId == route.Id && x.StopId == stop.Id);
            exists.Should().BeFalse();
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ExistsByRouteAndOrderIndexAsync_IsScopedToRoute()
    {
        var databaseName = $"vietride_trip_route_stop_order_{Guid.NewGuid():N}";
        await using var dbContext = CreateDbContext(databaseName);
        var repository = CreateRepository(dbContext);

        try
        {
            await dbContext.Database.MigrateAsync();
            var origin = Station.Create("Origin", $"origin-{Guid.NewGuid():N}", "Da Nang", "Da Nang");
            var destination = Station.Create("Destination", $"destination-{Guid.NewGuid():N}", "Hue", "Thua Thien Hue");
            var stop = Stop.Create(OperatorId, "Hai Van", 16.1m, 108.2m);
            var route = Route.Create(OperatorId, "Da Nang to Hue", origin.Id, destination.Id, Money.FromRaw(250000), 100m, 180);
            var otherRoute = Route.Create(OperatorId, "Hue to Da Nang", destination.Id, origin.Id, Money.FromRaw(250000), 100m, 180);
            dbContext.Stations.AddRange(origin, destination);
            dbContext.Stops.Add(stop);
            dbContext.Routes.AddRange(route, otherRoute);
            dbContext.RouteStops.Add(RouteStop.Create(route.Id, stop.Id, 3, 45, 25m, true, false));
            await dbContext.SaveChangesAsync();

            var sameRouteResult = await repository.ExistsByRouteAndOrderIndexAsync(route.Id, 3, CancellationToken.None);
            var otherRouteResult = await repository.ExistsByRouteAndOrderIndexAsync(otherRoute.Id, 3, CancellationToken.None);

            sameRouteResult.Should().BeTrue();
            otherRouteResult.Should().BeFalse();
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    private static IRouteStopRepository CreateRepository(TripDbContext dbContext)
    {
        var repositoryType = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Persistence.Repositories.RouteStopRepository",
            throwOnError: true)!;

        return (IRouteStopRepository)Activator.CreateInstance(repositoryType, dbContext)!;
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
