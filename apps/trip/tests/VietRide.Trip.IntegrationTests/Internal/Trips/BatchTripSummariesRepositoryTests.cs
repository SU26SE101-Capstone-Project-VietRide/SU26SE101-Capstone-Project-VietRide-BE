using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Internal.Trips.BatchTripSummaries;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.IntegrationTests.Internal.Trips;

public sealed class BatchTripSummariesRepositoryTests
{
    [Fact]
    public async Task BatchTripSummaries_RepositoryReturnsCrossOperatorHistoricalProjectionAndOmitsMissingIds()
    {
        var databaseName = $"{Day29CargoNearFullOutboxIntegrationTests.ScratchDatabasePrefix}{Guid.NewGuid():N}";
        await using var db = Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
        try
        {
            await db.Database.MigrateAsync();
            var seed = await SeedAsync(db);
            var repository = CreateRepository(db);
            var requestedIds = new[] { seed.SecondTripId, Guid.NewGuid(), seed.FirstTripId };

            var result = await repository.ListSummariesByIdsAsync(requestedIds, CancellationToken.None);

            result.Should().HaveCount(2);
            result.Select(item => item.TripId.ToString("D")).Should().BeInAscendingOrder();
            result.Select(item => item.TripId).Should().BeEquivalentTo(
                [seed.FirstTripId, seed.SecondTripId]);

            var first = result.Single(item => item.TripId == seed.FirstTripId);
            first.Status.Should().Be("SCHEDULED");
            first.DepartureAt.Should().Be(seed.FirstDeparture);
            first.ArrivalEstimate.Should().Be(seed.FirstArrival);
            first.Route.RouteId.Should().Be(seed.FirstRouteId);
            first.Route.Name.Should().Be("HCM - Da Lat");
            first.Route.OriginName.Should().Be("Ben xe Mien Dong");
            first.Route.DestinationName.Should().Be("Ben xe Da Lat");
            first.Vehicle.VehicleId.Should().Be(seed.FirstVehicleId);
            first.Vehicle.LicensePlate.Should().Be("51B-123.45");
            first.Vehicle.Status.Should().Be("MAINTENANCE");
            first.DriverUserId.Should().Be(seed.FirstDriverUserId);
            first.AssistantUserId.Should().BeNull();

            var second = result.Single(item => item.TripId == seed.SecondTripId);
            second.DriverUserId.Should().Be(seed.SecondDriverUserId);
            second.AssistantUserId.Should().Be(seed.SecondAssistantUserId);
        }
        finally
        {
            await Day29CargoNearFullOutboxIntegrationTests.DeleteScratchDatabaseAsync(db, databaseName);
        }
    }

    private static ITripRepository CreateRepository(TripDbContext db)
    {
        var type = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Persistence.Repositories.TripRepository",
            throwOnError: true)!;
        return (ITripRepository)Activator.CreateInstance(type, db)!;
    }

    private static async Task<Seed> SeedAsync(TripDbContext db)
    {
        var firstOperatorId = Guid.NewGuid();
        var secondOperatorId = Guid.NewGuid();
        var origin = Station.Create("Ben xe Mien Dong", "mien-dong-ui10", "Ho Chi Minh", "Ho Chi Minh");
        var destination = Station.Create("Ben xe Da Lat", "da-lat-ui10", "Da Lat", "Lam Dong");
        var firstRoute = Route.Create(
            firstOperatorId,
            "HCM - Da Lat",
            origin.Id,
            destination.Id,
            Money.FromRaw(300_000),
            300m,
            420);
        var secondRoute = Route.Create(
            secondOperatorId,
            "Da Lat - HCM",
            destination.Id,
            origin.Id,
            Money.FromRaw(310_000),
            300m,
            420);
        var vehicleType = VehicleType.Create("UI10", "UI-10 summary coach", 10, 1);
        var layout = JsonSerializer.SerializeToElement(new
        {
            version = 1,
            vehicleTypeCode = "UI10",
            totalSeats = 1,
            rows = 1,
            cols = 1,
            decks = 1,
            aisles = Array.Empty<object>(),
            seats = new[]
            {
                new
                {
                    seatNumber = "A01",
                    row = 1,
                    col = 1,
                    deck = 1,
                    type = "STANDARD",
                    isWindow = true,
                    isAisle = false,
                    disabled = false,
                },
            },
        });
        var firstVehicle = Vehicle.Create(
            firstOperatorId,
            vehicleType.Id,
            "51B-123.45",
            layout,
            1,
            null,
            null);
        firstVehicle.ChangeStatus(VehicleStatus.MAINTENANCE);
        var secondVehicle = Vehicle.Create(
            secondOperatorId,
            vehicleType.Id,
            "51B-543.21",
            layout,
            1,
            null,
            null);
        var firstDriverUserId = Guid.NewGuid();
        var secondDriverUserId = Guid.NewGuid();
        var secondAssistantUserId = Guid.NewGuid();
        var firstDeparture = DateTimeOffset.Parse("2026-07-29T01:00:00Z");
        var firstArrival = DateTimeOffset.Parse("2026-07-29T08:00:00Z");
        var secondDeparture = DateTimeOffset.Parse("2026-07-30T02:00:00Z");
        var firstTrip = TripEntity.Create(
            firstOperatorId,
            firstRoute.Id,
            firstVehicle.Id,
            firstDriverUserId,
            null,
            null,
            firstDeparture,
            firstArrival,
            TripSource.MANUAL,
            Money.FromRaw(300_000),
            null,
            maxCargoVolumeM3: null,
            estimatedPassengerLuggageKg: 0m,
            seatLayoutSnapshotJson: firstVehicle.SeatLayoutJson);
        var secondTrip = TripEntity.Create(
            secondOperatorId,
            secondRoute.Id,
            secondVehicle.Id,
            secondDriverUserId,
            secondAssistantUserId,
            null,
            secondDeparture,
            secondDeparture.AddHours(7),
            TripSource.MANUAL,
            Money.FromRaw(310_000),
            null,
            maxCargoVolumeM3: null,
            estimatedPassengerLuggageKg: 0m,
            seatLayoutSnapshotJson: secondVehicle.SeatLayoutJson);

        db.AddRange(
            origin,
            destination,
            firstRoute,
            secondRoute,
            vehicleType,
            firstVehicle,
            secondVehicle,
            firstTrip,
            secondTrip);
        await db.SaveChangesAsync();

        var deletedAt = DateTimeOffset.Parse("2026-07-29T00:00:00Z");
        firstRoute.SoftDelete(deletedAt);
        origin.SoftDelete(deletedAt);
        firstVehicle.SoftDelete(deletedAt);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return new Seed(
            firstTrip.Id,
            secondTrip.Id,
            firstRoute.Id,
            firstVehicle.Id,
            firstDriverUserId,
            secondDriverUserId,
            secondAssistantUserId,
            firstDeparture,
            firstArrival);
    }

    private sealed record Seed(
        Guid FirstTripId,
        Guid SecondTripId,
        Guid FirstRouteId,
        Guid FirstVehicleId,
        Guid FirstDriverUserId,
        Guid SecondDriverUserId,
        Guid SecondAssistantUserId,
        DateTimeOffset FirstDeparture,
        DateTimeOffset FirstArrival);
}
