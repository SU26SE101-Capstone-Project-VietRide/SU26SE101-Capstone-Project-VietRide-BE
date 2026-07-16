using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests.Persistence;

public sealed class RouteStopFareTemplateRepositoryPersistenceTests
{
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset EffectiveFrom = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExistsOverlappingAsync_TreatsOpenEndedEffectiveUntilAsInfinity()
    {
        var databaseName = $"vietride_trip_fare_template_overlap_{Guid.NewGuid():N}";
        await using var dbContext = CreateDbContext(databaseName);
        var repository = CreateRepository(dbContext);

        try
        {
            await dbContext.Database.MigrateAsync();
            var (route, stop) = await SeedRouteStopAsync(dbContext);
            dbContext.RouteStopFareTemplates.Add(RouteStopFareTemplate.Create(
                route.Id,
                stop.Id,
                Money.FromRaw(100000),
                EffectiveFrom,
                null));
            await dbContext.SaveChangesAsync();

            var overlaps = await repository.ExistsOverlappingAsync(
                route.Id,
                stop.Id,
                EffectiveFrom.AddDays(30),
                EffectiveFrom.AddDays(40),
                CancellationToken.None);

            overlaps.Should().BeTrue();

            dbContext.RouteStopFareTemplates.Add(RouteStopFareTemplate.Create(
                route.Id,
                stop.Id,
                Money.FromRaw(120000),
                EffectiveFrom.AddDays(30),
                EffectiveFrom.AddDays(40)));
            var save = () => dbContext.SaveChangesAsync();
            (await save.Should().ThrowAsync<DbUpdateException>())
                .Which.InnerException.Should().BeOfType<PostgresException>()
                .Which.SqlState.Should().Be(PostgresErrorCodes.ExclusionViolation);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ExistsOverlappingAsync_AllowsAdjacentNonOverlappingWindows()
    {
        var databaseName = $"vietride_trip_fare_template_non_overlap_{Guid.NewGuid():N}";
        await using var dbContext = CreateDbContext(databaseName);
        var repository = CreateRepository(dbContext);

        try
        {
            await dbContext.Database.MigrateAsync();
            var (route, stop) = await SeedRouteStopAsync(dbContext);
            dbContext.RouteStopFareTemplates.Add(RouteStopFareTemplate.Create(
                route.Id,
                stop.Id,
                Money.FromRaw(100000),
                EffectiveFrom,
                EffectiveFrom.AddDays(10)));
            await dbContext.SaveChangesAsync();

            var overlaps = await repository.ExistsOverlappingAsync(
                route.Id,
                stop.Id,
                EffectiveFrom.AddDays(10),
                EffectiveFrom.AddDays(20),
                CancellationToken.None);

            overlaps.Should().BeFalse();

            dbContext.RouteStopFareTemplates.Add(RouteStopFareTemplate.Create(
                route.Id,
                stop.Id,
                Money.FromRaw(120000),
                EffectiveFrom.AddDays(10),
                EffectiveFrom.AddDays(20)));
            await dbContext.SaveChangesAsync();
            (await dbContext.RouteStopFareTemplates.CountAsync()).Should().Be(2);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<(Route Route, Stop Stop)> SeedRouteStopAsync(TripDbContext dbContext)
    {
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

        return (route, stop);
    }

    private static IRouteStopFareTemplateRepository CreateRepository(TripDbContext dbContext)
    {
        var repositoryType = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Persistence.Repositories.RouteStopFareTemplateRepository",
            throwOnError: true)!;

        return (IRouteStopFareTemplateRepository)Activator.CreateInstance(repositoryType, dbContext)!;
    }

    private static TripDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(warnings => warnings.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
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
