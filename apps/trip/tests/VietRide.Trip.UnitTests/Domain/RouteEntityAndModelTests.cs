using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.UnitTests.Domain;

public sealed class RouteEntityAndModelTests
{
    [Fact]
    public void Route_Create_KeepsMoneyToTheDongAndAllowsNullableDistanceAndDuration()
    {
        var route = Route.Create(
            Guid.NewGuid(),
            "  Main corridor  ",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Money.FromRaw(123_456),
            totalDistanceKm: null,
            estimatedDurationMinutes: null);

        route.Name.Should().Be("Main corridor");
        route.BaseFare.Amount.Should().Be(123_456);
        route.TotalDistanceKm.Should().BeNull();
        route.EstimatedDurationMinutes.Should().BeNull();
        route.IsActive.Should().BeTrue();
        route.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void Route_Create_RejectsBlankName()
    {
        var act = () => Route.Create(
            Guid.NewGuid(),
            "   ",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Money.FromRaw(100_000),
            totalDistanceKm: null,
            estimatedDurationMinutes: null);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("name");
    }

    [Fact]
    public void Route_Create_RejectsSameOriginAndDestination()
    {
        var stationId = Guid.NewGuid();

        var act = () => Route.Create(
            Guid.NewGuid(),
            "Main corridor",
            stationId,
            stationId,
            Money.FromRaw(100_000),
            totalDistanceKm: null,
            estimatedDurationMinutes: null);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("destinationStationId");
    }

    [Fact]
    public void RouteStop_Create_RejectsBothPickupAndDropoffFalse()
    {
        var act = () => RouteStop.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            orderIndex: 1,
            estimatedDurationFromOriginMinutes: 30,
            distanceFromOriginKm: null,
            allowPickup: false,
            allowDropoff: false);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("allowPickup");
    }

    [Fact]
    public void AlternativeRoute_Deactivate_SetsIsActiveFalseWithoutSoftDeleteContract()
    {
        var alternativeRoute = AlternativeRoute.Create(
            Guid.NewGuid(),
            "Congestion bypass",
            Guid.NewGuid(),
            totalDistanceKm: null,
            estimatedDurationMinutes: null);

        alternativeRoute.Deactivate();

        alternativeRoute.IsActive.Should().BeFalse();
        alternativeRoute.Should().BeAssignableTo<IActivatable>();
        alternativeRoute.Should().NotBeAssignableTo<ISoftDeletable>();
    }

    [Fact]
    public void TripModel_MapsDay8TablesWithKeysIndexesAndChecks()
    {
        using var dbContext = CreateDbContext();
        var model = dbContext.GetService<IDesignTimeModel>().Model;

        var route = model.FindEntityType(typeof(Route));
        route.Should().NotBeNull();
        var routeEntity = route!;
        routeEntity.GetTableName().Should().Be("routes");
        routeEntity.FindProperty(nameof(Route.OperatorId))!.GetColumnName().Should().Be("operator_id");
        routeEntity.FindProperty(nameof(Route.Name))!.GetColumnName().Should().Be("name");
        routeEntity.FindProperty(nameof(Route.Name))!.GetMaxLength().Should().Be(255);
        routeEntity.FindProperty(nameof(Route.Name))!.IsNullable.Should().BeFalse();
        routeEntity.FindProperty(nameof(Route.BaseFare))!.GetColumnType().Should().Be("bigint");
        routeEntity.FindProperty(nameof(Route.DeletedAt)).Should().NotBeNull();
        routeEntity.FindProperty(nameof(Route.IsActive)).Should().NotBeNull();
        routeEntity.FindProperty(nameof(Route.TotalDistanceKm))!.IsNullable.Should().BeTrue();
        routeEntity.FindProperty(nameof(Route.EstimatedDurationMinutes))!.IsNullable.Should().BeTrue();
        routeEntity.GetIndexes().Select(index => index.GetDatabaseName()).Should().BeEquivalentTo(new[]
        {
            "idx_routes_operator_id",
            "idx_routes_return_route_id",
            "idx_routes_origin_destination"
        });
        routeEntity.GetCheckConstraints().Select(check => check.Name).Should().Contain(new[]
        {
            "chk_routes_origin_dest_different",
            "chk_routes_base_fare_non_negative"
        });

        var routeStop = model.FindEntityType(typeof(RouteStop));
        routeStop.Should().NotBeNull();
        var routeStopEntity = routeStop!;
        routeStopEntity.GetTableName().Should().Be("route_stops");
        routeStopEntity.FindPrimaryKey()!.GetName().Should().Be("pk_route_stops");
        routeStopEntity.FindPrimaryKey()!.Properties.Select(property => property.GetColumnName()).Should().Equal("route_id", "stop_id");
        routeStopEntity.FindProperty(nameof(RouteStop.DistanceFromOriginKm))!.IsNullable.Should().BeTrue();
        routeStopEntity.FindProperty("DeletedAt").Should().BeNull();
        routeStopEntity.GetIndexes().Should().Contain(index =>
            index.IsUnique
            && index.GetDatabaseName() == "uq_route_stops_route_order"
            && index.Properties.Select(property => property.GetColumnName()).SequenceEqual(new[] { "route_id", "order_index" }));
        routeStopEntity.GetCheckConstraints().Select(check => check.Name).Should().Contain(new[]
        {
            "chk_route_stops_allow_at_least_one",
            "chk_route_stops_order_positive"
        });

        var fareTemplate = model.FindEntityType(typeof(RouteStopFareTemplate));
        fareTemplate.Should().NotBeNull();
        var fareTemplateEntity = fareTemplate!;
        fareTemplateEntity.GetTableName().Should().Be("route_stop_fare_templates");
        fareTemplateEntity.FindProperty(nameof(RouteStopFareTemplate.FareFromThisStop))!.GetColumnType().Should().Be("bigint");
        fareTemplateEntity.FindProperty("DeletedAt").Should().BeNull();
        fareTemplateEntity.GetIndexes().Should().Contain(index =>
            index.GetDatabaseName() == "idx_route_stop_fare_templates_route_stop_effective"
            && index.Properties.Select(property => property.GetColumnName()).SequenceEqual(new[] { "route_id", "stop_id", "effective_from" }));
        fareTemplateEntity.GetCheckConstraints().Select(check => check.Name).Should().Contain(new[]
        {
            "chk_route_stop_fare_templates_fare_non_negative",
            "chk_route_stop_fare_templates_effective_order"
        });

        var alternativeRoute = model.FindEntityType(typeof(AlternativeRoute));
        alternativeRoute.Should().NotBeNull();
        var alternativeRouteEntity = alternativeRoute!;
        alternativeRouteEntity.GetTableName().Should().Be("alternative_routes");
        alternativeRouteEntity.FindProperty(nameof(AlternativeRoute.IsActive)).Should().NotBeNull();
        alternativeRouteEntity.FindProperty("DeletedAt").Should().BeNull();
        alternativeRouteEntity.FindProperty(nameof(AlternativeRoute.TotalDistanceKm))!.IsNullable.Should().BeTrue();
        alternativeRouteEntity.FindProperty(nameof(AlternativeRoute.EstimatedDurationMinutes))!.IsNullable.Should().BeTrue();
        alternativeRouteEntity.GetIndexes().Should().Contain(index =>
            index.GetDatabaseName() == "idx_alternative_routes_route_id"
            && index.GetFilter() == "is_active = TRUE");

        var alternativeRouteStop = model.FindEntityType(typeof(AlternativeRouteStop));
        alternativeRouteStop.Should().NotBeNull();
        var alternativeRouteStopEntity = alternativeRouteStop!;
        alternativeRouteStopEntity.GetTableName().Should().Be("alternative_route_stops");
        alternativeRouteStopEntity.FindPrimaryKey()!.GetName().Should().Be("pk_alternative_route_stops");
        alternativeRouteStopEntity.FindPrimaryKey()!.Properties.Select(property => property.GetColumnName()).Should().Equal("alternative_route_id", "stop_id");
        alternativeRouteStopEntity.FindProperty(nameof(AlternativeRouteStop.DistanceFromOriginKm))!.IsNullable.Should().BeTrue();
        alternativeRouteStopEntity.FindProperty("DeletedAt").Should().BeNull();
        alternativeRouteStopEntity.GetIndexes().Should().Contain(index =>
            index.IsUnique
            && index.GetDatabaseName() == "uq_alternative_route_stops_route_order"
            && index.Properties.Select(property => property.GetColumnName()).SequenceEqual(new[] { "alternative_route_id", "order_index" }));
        alternativeRouteStopEntity.GetCheckConstraints().Select(check => check.Name).Should().Contain("chk_alternative_route_stops_order_positive");
    }

    private static TripDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .UseNpgsql("Host=localhost;Database=vietride_trip_unit;Username=postgres;Password=postgres")
            .Options;

        return new TripDbContext(options, new FrozenClock());
    }

    private sealed class FrozenClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);
    }
}
