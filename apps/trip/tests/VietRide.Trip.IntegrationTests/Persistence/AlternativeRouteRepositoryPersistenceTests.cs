using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests.Persistence;

public sealed class AlternativeRouteRepositoryPersistenceTests
{
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherOperatorId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task CountActiveByRouteAsync_CountsOnlyActiveAlternativeRoutes()
    {
        var databaseName = $"vietride_trip_alt_route_active_{Guid.NewGuid():N}";
        await using var dbContext = CreateDbContext(databaseName);
        var repository = CreateRepository(dbContext);

        try
        {
            await dbContext.Database.MigrateAsync();
            var (route, destination, _) = await SeedRouteAsync(dbContext, OperatorId);
            var active = AlternativeRoute.Create(route.Id, "Active bypass", destination.Id, null, null);
            var inactive = AlternativeRoute.Create(route.Id, "Inactive bypass", destination.Id, null, null);
            inactive.Deactivate();
            dbContext.AlternativeRoutes.AddRange(active, inactive);
            await dbContext.SaveChangesAsync();

            var count = await repository.CountActiveByRouteAsync(route.Id, CancellationToken.None);

            count.Should().Be(1);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task DeactivateAlternativeRoute_KeepsRowPresent()
    {
        var databaseName = $"vietride_trip_alt_route_deactivate_{Guid.NewGuid():N}";
        await using var dbContext = CreateDbContext(databaseName);
        var repository = CreateRepository(dbContext);

        try
        {
            await dbContext.Database.MigrateAsync();
            var (route, destination, _) = await SeedRouteAsync(dbContext, OperatorId);
            var alternativeRoute = AlternativeRoute.Create(route.Id, "Incident bypass", destination.Id, null, null);
            dbContext.AlternativeRoutes.Add(alternativeRoute);
            await dbContext.SaveChangesAsync();

            alternativeRoute.Deactivate();
            repository.Update(alternativeRoute);
            await dbContext.SaveChangesAsync();

            var persisted = await dbContext.AlternativeRoutes.SingleOrDefaultAsync(x => x.Id == alternativeRoute.Id);
            persisted.Should().NotBeNull();
            persisted!.IsActive.Should().BeFalse();
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task GetOwnedByIdAsync_ReturnsNull_ForCrossOperatorAlternativeRoute()
    {
        var databaseName = $"vietride_trip_alt_route_tenant_{Guid.NewGuid():N}";
        await using var dbContext = CreateDbContext(databaseName);
        var repository = CreateRepository(dbContext);

        try
        {
            await dbContext.Database.MigrateAsync();
            var (otherRoute, destination, _) = await SeedRouteAsync(dbContext, OtherOperatorId);
            var alternativeRoute = AlternativeRoute.Create(otherRoute.Id, "Other bypass", destination.Id, null, null);
            dbContext.AlternativeRoutes.Add(alternativeRoute);
            await dbContext.SaveChangesAsync();

            var result = await repository.GetOwnedByIdAsync(OperatorId, alternativeRoute.Id, CancellationToken.None);

            result.Should().BeNull();
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ReplaceStopsAsync_PersistsIndependentStopSequence()
    {
        var databaseName = $"vietride_trip_alt_route_stops_{Guid.NewGuid():N}";
        await using var dbContext = CreateDbContext(databaseName);
        var repository = CreateRepository(dbContext);

        try
        {
            await dbContext.Database.MigrateAsync();
            var (route, destination, stop) = await SeedRouteAsync(dbContext, OperatorId);
            var alternativeRoute = AlternativeRoute.Create(route.Id, "Incident bypass", destination.Id, null, null);
            dbContext.AlternativeRoutes.Add(alternativeRoute);
            await dbContext.SaveChangesAsync();
            var alternativeRouteStop = AlternativeRouteStop.Create(alternativeRoute.Id, stop.Id, 2, 35, 12m);

            await repository.ReplaceStopsAsync(alternativeRoute.Id, [alternativeRouteStop], CancellationToken.None);
            await dbContext.SaveChangesAsync();

            var persistedStops = await repository.ListStopsAsync(alternativeRoute.Id, CancellationToken.None);
            persistedStops.Should().ContainSingle().Which.OrderIndex.Should().Be(2);
            var orderExists = await repository.ExistsStopOrderIndexAsync(alternativeRoute.Id, 2, CancellationToken.None);
            orderExists.Should().BeTrue();
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<(Route Route, Station Destination, Stop Stop)> SeedRouteAsync(
        TripDbContext dbContext,
        Guid operatorId)
    {
        var origin = Station.Create("Origin", $"origin-{Guid.NewGuid():N}", "Da Nang", "Da Nang");
        var destination = Station.Create("Destination", $"destination-{Guid.NewGuid():N}", "Hue", "Thua Thien Hue");
        var stop = Stop.Create(operatorId, $"Stop {Guid.NewGuid():N}", 16.1m, 108.2m);
        var route = Route.Create(operatorId, "Da Nang to Hue", origin.Id, destination.Id, Money.FromRaw(250000), 100m, 180);
        dbContext.Stations.AddRange(origin, destination);
        dbContext.Stops.Add(stop);
        dbContext.Routes.Add(route);
        await dbContext.SaveChangesAsync();
        return (route, destination, stop);
    }

    private static IAlternativeRouteRepository CreateRepository(TripDbContext dbContext)
    {
        var repositoryType = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Persistence.Repositories.AlternativeRouteRepository",
            throwOnError: true)!;

        return (IAlternativeRouteRepository)Activator.CreateInstance(repositoryType, dbContext)!;
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
