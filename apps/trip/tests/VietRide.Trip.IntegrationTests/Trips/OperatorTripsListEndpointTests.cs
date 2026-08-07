using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Trips.ListOperatorTrips;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;
using VietRide.Trip.IntegrationTests.Internal.Trips;
using DomainTrip = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.IntegrationTests.Trips;

public sealed class OperatorTripsListEndpointTests
{
    private const string TestSecret = "test-secret-at-least-32-chars-long-xxxxx";

    [Fact]
    public async Task OperatorAdmin_ReturnsEnvelopeAndDispatchesJwtTenantWithFilters()
    {
        var operatorId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var result = PagedResult<OperatorTripListItemDto>.Create(
            [new OperatorTripListItemDto(
                tripId,
                "IN_PROGRESS",
                new OperatorTripRouteDto(Guid.NewGuid(), "HCM - Đà Lạt", "Hồ Chí Minh", "Đà Lạt"),
                new OperatorTripVehicleDto(Guid.NewGuid(), "51B-123.45", "MAINTENANCE"),
                new OperatorTripCrewDto(Guid.NewGuid(), "Nguyễn Văn A", "0900000000"),
                null,
                DateTimeOffset.Parse("2026-07-29T01:00:00Z"),
                DateTimeOffset.Parse("2026-07-29T08:00:00Z"),
                true)],
            1,
            20,
            1);
        var mediator = new StubMediator(_ => result);
        using var factory = new OperatorTripsEndpointFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateRequest(
            "/v1/operator/trips?search=51B12345&status=IN_PROGRESS&from=2026-07-29&to=2026-07-30&page=1&pageSize=20&sortBy=departureAt&sortDir=desc",
            "OPERATOR_ADMIN",
            operatorId);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var item = document.RootElement.GetProperty("data").GetProperty("items")[0];
        item.GetProperty("tripId").GetGuid().Should().Be(tripId);
        item.GetProperty("canSubstituteVehicle").GetBoolean().Should().BeTrue();
        item.GetProperty("driver").GetProperty("phone").GetString().Should().Be("0900000000");

        mediator.LastRequest.Should().BeOfType<ListOperatorTripsQuery>();
        var query = (ListOperatorTripsQuery)mediator.LastRequest!;
        query.OperatorId.Should().Be(operatorId);
        query.Search.Should().Be("51B12345");
        query.Status.Should().Be(OperatorTripStatusFilter.IN_PROGRESS);
        query.From.Should().Be(new DateOnly(2026, 7, 29));
        query.To.Should().Be(new DateOnly(2026, 7, 30));
        query.Page.Should().Be(1);
        query.PageSize.Should().Be(20);
        query.SortBy.Should().Be("departureAt");
        query.SortDir.Should().Be("desc");
    }

    [Fact]
    public async Task OperatorStaff_IsForbiddenBeforeDispatch()
    {
        var mediator = new StubMediator(_ => throw new InvalidOperationException("Must not dispatch."));
        using var factory = new OperatorTripsEndpointFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateRequest("/v1/operator/trips", "OPERATOR_STAFF", Guid.NewGuid());

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        mediator.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task InvalidTripStatus_ReturnsValidationEnvelopeBeforeDispatch()
    {
        var mediator = new StubMediator(_ => throw new InvalidOperationException("Must not dispatch."));
        using var factory = new OperatorTripsEndpointFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateRequest(
            "/v1/operator/trips?status=UNKNOWN",
            "OPERATOR_ADMIN",
            Guid.NewGuid());

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("VALIDATION_ERROR");
        mediator.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task Repository_FiltersTenantInclusiveRangeNormalizedPlateAndRouteName()
    {
        var databaseName = $"{Day29CargoNearFullOutboxIntegrationTests.ScratchDatabasePrefix}{Guid.NewGuid():N}";
        await using var db = Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
        try
        {
            await db.Database.MigrateAsync();
            var operatorId = Guid.NewGuid();
            var foreignOperatorId = Guid.NewGuid();
            var seed = await SeedTripsAsync(db, operatorId, foreignOperatorId);
            await SoftDeleteHistoricalMetadataAsync(db, seed);
            var repository = CreateRepository(db);
            var fromUtc = DateTimeOffset.Parse("2026-07-28T17:00:00Z");
            var toUtc = DateTimeOffset.Parse("2026-07-30T17:00:00Z");

            var byPlate = await repository.ListOperatorTripsAsync(
                operatorId,
                1,
                20,
                "51B12345",
                "51B12345",
                TripStatus.IN_PROGRESS,
                fromUtc,
                toUtc,
                true,
                CancellationToken.None);

            byPlate.Items.Should().ContainSingle().Which.TripId.Should().Be(seed.ExpectedTripId);
            byPlate.Items[0].LicensePlate.Should().Be("51B–123_45");
            byPlate.Items[0].OriginName.Should().Be("Bến xe Miền Đông");
            byPlate.Items[0].DestinationName.Should().Be("Bến xe Đà Lạt");
            byPlate.Items[0].VehicleStatus.Should().Be(VehicleStatus.MAINTENANCE);

            var byRoute = await repository.ListOperatorTripsAsync(
                operatorId,
                1,
                20,
                "đà lạt",
                "ĐÀLẠT",
                null,
                fromUtc,
                toUtc,
                false,
                CancellationToken.None);

            byRoute.Items.Should().ContainSingle().Which.TripId.Should().Be(seed.ExpectedTripId);
            byRoute.Items.Should().NotContain(item => item.TripId == seed.ForeignTripId);
            byRoute.Items.Should().NotContain(item => item.TripId == seed.ExclusiveBoundaryTripId);
        }
        finally
        {
            await Day29CargoNearFullOutboxIntegrationTests.DeleteScratchDatabaseAsync(db, databaseName);
        }
    }

    private static async Task SoftDeleteHistoricalMetadataAsync(TripDbContext db, RepositorySeed seed)
    {
        var deletedAt = DateTimeOffset.Parse("2026-07-29T02:00:00Z");
        var route = await db.Routes.SingleAsync(item => item.Id == seed.RouteId);
        var vehicle = await db.Vehicles.SingleAsync(item => item.Id == seed.VehicleId);
        var station = await db.Stations.SingleAsync(item => item.Id == seed.OriginStationId);
        route.SoftDelete(deletedAt);
        vehicle.SoftDelete(deletedAt);
        station.SoftDelete(deletedAt);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static ITripRepository CreateRepository(TripDbContext db)
    {
        var type = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Persistence.Repositories.TripRepository",
            throwOnError: true)!;
        return (ITripRepository)Activator.CreateInstance(type, db)!;
    }

    private static async Task<RepositorySeed> SeedTripsAsync(
        TripDbContext db,
        Guid operatorId,
        Guid foreignOperatorId)
    {
        var origin = Station.Create("Bến xe Miền Đông", "mien-dong", "Hồ Chí Minh", "Hồ Chí Minh");
        var destination = Station.Create("Bến xe Đà Lạt", "da-lat", "Đà Lạt", "Lâm Đồng");
        var route = Route.Create(
            operatorId,
            "HCM - Đà Lạt",
            origin.Id,
            destination.Id,
            Money.FromRaw(300_000),
            300m,
            420);
        var foreignRoute = Route.Create(
            foreignOperatorId,
            "HCM - Đà Lạt",
            origin.Id,
            destination.Id,
            Money.FromRaw(300_000),
            300m,
            420);
        var vehicleType = VehicleType.Create("UI03", "UI-03 test coach", 10, 1);
        var layout = JsonSerializer.SerializeToElement(new
        {
            version = 1,
            vehicleTypeCode = "UI03",
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
        var vehicle = Vehicle.Create(operatorId, vehicleType.Id, "51B–123_45", layout, 1, null, null);
        vehicle.ChangeStatus(VehicleStatus.MAINTENANCE);
        var boundaryVehicle = Vehicle.Create(operatorId, vehicleType.Id, "51B-999.99", layout, 1, null, null);
        var foreignVehicle = Vehicle.Create(foreignOperatorId, vehicleType.Id, "51B-123.46", layout, 1, null, null);
        var departure = DateTimeOffset.Parse("2026-07-29T01:00:00Z");
        var expected = CreateInProgressTrip(operatorId, route.Id, vehicle.Id, departure, layout);
        var exclusiveBoundary = CreateInProgressTrip(
            operatorId,
            route.Id,
            boundaryVehicle.Id,
            DateTimeOffset.Parse("2026-07-30T17:00:00Z"),
            layout);
        var foreign = CreateInProgressTrip(
            foreignOperatorId,
            foreignRoute.Id,
            foreignVehicle.Id,
            departure,
            layout);

        db.AddRange(
            origin,
            destination,
            route,
            foreignRoute,
            vehicleType,
            vehicle,
            boundaryVehicle,
            foreignVehicle,
            expected,
            exclusiveBoundary,
            foreign);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new RepositorySeed(
            expected.Id,
            exclusiveBoundary.Id,
            foreign.Id,
            route.Id,
            vehicle.Id,
            origin.Id);
    }

    private static DomainTrip CreateInProgressTrip(
        Guid operatorId,
        Guid routeId,
        Guid vehicleId,
        DateTimeOffset departure,
        JsonElement seatLayoutSnapshotJson)
    {
        var trip = DomainTrip.Create(
            operatorId,
            routeId,
            vehicleId,
            Guid.NewGuid(),
            null,
            null,
            departure,
            departure.AddHours(7),
            TripSource.MANUAL,
            Money.FromRaw(300_000),
            null,
            maxCargoVolumeM3: null,
            estimatedPassengerLuggageKg: 0m,
            seatLayoutSnapshotJson: seatLayoutSnapshotJson);
        trip.MarkBoarding(departure.AddMinutes(-30));
        trip.Start(departure);
        return trip;
    }

    private static HttpRequestMessage CreateRequest(string path, string role, Guid operatorId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt(role, operatorId)}");
        return request;
    }

    private static string CreateInternalJwt(string role, Guid operatorId)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims:
            [
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, role),
                new Claim("operatorId", operatorId.ToString()),
            ],
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class OperatorTripsEndpointFactory : WebApplicationFactory<Program>
    {
        private readonly IMediator mediator;

        public OperatorTripsEndpointFactory(IMediator mediator)
        {
            this.mediator = mediator;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("INTERNAL_JWT_SECRET", TestSecret);
            builder.UseSetting("Trip:BackgroundWorkers:Enabled", "false");
            builder.UseSetting(
                "ConnectionStrings:Default",
                VietRideWebApplicationFactory.ResolveConnectionString("postgres"));
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMediator>();
                services.AddSingleton(mediator);
            });
        }
    }

    private sealed class StubMediator : IMediator
    {
        private readonly Func<object, object?> responder;

        public StubMediator(Func<object, object?> responder)
        {
            this.responder = responder;
        }

        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            var response = responder(request);
            return Task.FromResult(response is TResponse typed ? typed : default!);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(responder(request));
        }

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => EmptyStream<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            EmptyStream<object?>();

        private static async IAsyncEnumerable<T> EmptyStream<T>()
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed record RepositorySeed(
        Guid ExpectedTripId,
        Guid ExclusiveBoundaryTripId,
        Guid ForeignTripId,
        Guid RouteId,
        Guid VehicleId,
        Guid OriginStationId);
}
