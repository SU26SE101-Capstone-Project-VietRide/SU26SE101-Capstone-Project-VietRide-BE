using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests.Persistence;

public sealed class RouteRepositoryTenantIsolationTests
{
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherOperatorId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task GetOwnedByIdAsync_ReturnsNull_ForCrossOperatorRoute()
    {
        var databaseName = $"vietride_trip_route_tenant_{Guid.NewGuid():N}";
        await using var dbContext = CreateDbContext(databaseName);
        var repository = CreateRepository(dbContext);

        try
        {
            await dbContext.Database.MigrateAsync();
            var origin = Station.Create("Origin", $"origin-{Guid.NewGuid():N}", "Da Nang", "Da Nang");
            var destination = Station.Create("Destination", $"destination-{Guid.NewGuid():N}", "Hue", "Thua Thien Hue");
            var otherRoute = Route.Create(OtherOperatorId, "Other route", origin.Id, destination.Id, Money.FromRaw(250000), 100m, 180);
            dbContext.Stations.AddRange(origin, destination);
            dbContext.Routes.Add(otherRoute);
            await dbContext.SaveChangesAsync();

            var result = await repository.GetOwnedByIdAsync(OperatorId, otherRoute.Id, CancellationToken.None);

            result.Should().BeNull();
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task GetOwnedByIdAsync_ReturnsInactiveOwnedRoute_WhileActiveLookupDoesNot()
    {
        var databaseName = $"vietride_trip_route_inactive_{Guid.NewGuid():N}";
        await using var dbContext = CreateDbContext(databaseName);
        var repository = CreateRepository(dbContext);

        try
        {
            await dbContext.Database.MigrateAsync();
            var origin = Station.Create("Origin", $"origin-{Guid.NewGuid():N}", "Da Nang", "Da Nang");
            var destination = Station.Create("Destination", $"destination-{Guid.NewGuid():N}", "Hue", "Thua Thien Hue");
            var inactiveRoute = Route.Create(OperatorId, "Inactive route", origin.Id, destination.Id, Money.FromRaw(250000), 100m, 180);
            inactiveRoute.Deactivate();
            dbContext.Stations.AddRange(origin, destination);
            dbContext.Routes.Add(inactiveRoute);
            await dbContext.SaveChangesAsync();

            var ownedResult = await repository.GetOwnedByIdAsync(OperatorId, inactiveRoute.Id, CancellationToken.None);
            var activeResult = await repository.GetOwnedActiveByIdAsync(OperatorId, inactiveRoute.Id, CancellationToken.None);

            ownedResult.Should().NotBeNull();
            ownedResult!.Id.Should().Be(inactiveRoute.Id);
            activeResult.Should().BeNull();
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    private static IRouteRepository CreateRepository(TripDbContext dbContext)
    {
        var repositoryType = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Persistence.Repositories.RouteRepository",
            throwOnError: true)!;

        return (IRouteRepository)Activator.CreateInstance(repositoryType, dbContext)!;
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
