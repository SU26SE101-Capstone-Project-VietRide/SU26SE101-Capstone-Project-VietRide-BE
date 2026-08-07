using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Internal.Trips.GetTripSnapshot;
using VietRide.Trip.Application.Features.Trips.Operations;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests.DriverTrips;

public sealed class TripArrivalEndpointTests
{
    private const string TestSecret = "test-secret-at-least-32-chars-long-xxxxx";
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConcurrentStopArrivalRequests_ProduceOne200One409AndOneTimestampEvent()
    {
        var databaseName = $"vietride_trip_stop_arrival_race_{Guid.NewGuid():N}";
        await using var setup = CreateDbContext(databaseName);
        try
        {
            await setup.Database.MigrateAsync();
            var seeded = await SeedTripAsync(setup, includeStop: true);
            var stop = seeded.TripStop!;
            using var factory = new ArrivalWebApplicationFactory(new ArrivalDatabaseMediator(databaseName));
            using var client = factory.CreateClient();
            var path = $"/v1/driver/trips/{seeded.Trip.Id}/stops/{stop.StopId}/arrive";

            var responses = await Task.WhenAll(
                client.SendAsync(CreateRequest(path, "ASSISTANT", seeded.Trip.AssistantUserId!.Value, NewKey())),
                client.SendAsync(CreateRequest(path, "ASSISTANT", seeded.Trip.AssistantUserId!.Value, NewKey())));

            await AssertOneWinnerOneConflictAsync(responses, "TRIP_STOP_ALREADY_FINALIZED");
            await using var assertionDb = CreateDbContext(databaseName);
            var persisted = await assertionDb.TripStops.SingleAsync(item =>
                item.TripId == seeded.Trip.Id && item.StopId == stop.StopId);
            persisted.Status.Should().Be(TripStopStatus.ARRIVED);
            persisted.ActualArrivalTime.Should().Be(Now);
            persisted.EstimatedArrivalTime.Should().Be(stop.EstimatedArrivalTime);
            var outbox = await assertionDb.OutboxEvents.SingleAsync(item =>
                item.EventType == "trip.stop.arrived");
            AssertStopArrivedPayload(
                outbox.Payload,
                seeded.Trip.Id,
                stop.StopId,
                seeded.Trip.OperatorId,
                seeded.Trip.AssistantUserId.Value);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ConcurrentExpressDestinationArrivalRequests_ProduceOne200One409AndIndependentAnchor()
    {
        var databaseName = $"vietride_trip_destination_arrival_race_{Guid.NewGuid():N}";
        await using var setup = CreateDbContext(databaseName);
        try
        {
            await setup.Database.MigrateAsync();
            var seeded = await SeedTripAsync(setup, includeStop: false);
            using var factory = new ArrivalWebApplicationFactory(new ArrivalDatabaseMediator(databaseName));
            using var client = factory.CreateClient();
            var path = $"/v1/driver/trips/{seeded.Trip.Id}/destination/arrive";

            var responses = await Task.WhenAll(
                client.SendAsync(CreateRequest(path, "DRIVER", seeded.Trip.DriverUserId, NewKey())),
                client.SendAsync(CreateRequest(path, "DRIVER", seeded.Trip.DriverUserId, NewKey())));

            await AssertOneWinnerOneConflictAsync(responses, "TRIP_DESTINATION_ALREADY_ARRIVED");
            await using var assertionDb = CreateDbContext(databaseName);
            var persisted = await assertionDb.Trips.SingleAsync(item => item.Id == seeded.Trip.Id);
            persisted.DestinationArrivedAt.Should().Be(Now);
            persisted.DestinationArrivedByUserId.Should().Be(seeded.Trip.DriverUserId);
            persisted.CompletedAt.Should().BeNull();
            persisted.Status.Should().Be(TripStatus.IN_PROGRESS);
            (await assertionDb.TripStops.CountAsync(item => item.TripId == seeded.Trip.Id))
                .Should().Be(0);
            var snapshot = await new GetTripSnapshotHandler(
                CreateRepository<ITripRepository>(
                    assertionDb,
                    "VietRide.Trip.Infrastructure.Persistence.Repositories.TripRepository"),
                CreateRepository<IRouteRepository>(
                    assertionDb,
                    "VietRide.Trip.Infrastructure.Persistence.Repositories.RouteRepository"),
                CreateRepository<IRouteStopFareTemplateRepository>(
                    assertionDb,
                    "VietRide.Trip.Infrastructure.Persistence.Repositories.RouteStopFareTemplateRepository"),
                CreateRepository<IStationRepository>(
                    assertionDb,
                    "VietRide.Trip.Infrastructure.Persistence.Repositories.StationRepository"),
                CreateRepository<IStopRepository>(
                    assertionDb,
                    "VietRide.Trip.Infrastructure.Persistence.Repositories.StopRepository"),
                CreateRepository<ITripSeatRepository>(
                    assertionDb,
                    "VietRide.Trip.Infrastructure.Persistence.Repositories.TripSeatRepository"),
                CreateRepository<ITripStopRepository>(
                    assertionDb,
                    "VietRide.Trip.Infrastructure.Persistence.Repositories.TripStopRepository"),
                CreateRepository<ITripStopFareRepository>(
                    assertionDb,
                    "VietRide.Trip.Infrastructure.Persistence.Repositories.TripStopFareRepository"))
                .Handle(new GetTripSnapshotQuery(seeded.Trip.Id), CancellationToken.None);
            snapshot.DestinationArrivedAt.Should().Be(Now);
            var outbox = await assertionDb.OutboxEvents.SingleAsync(item =>
                item.EventType == "trip.destination.arrived");
            AssertDestinationArrivedPayload(
                outbox.Payload,
                seeded.Trip.Id,
                seeded.Route.DestinationStationId,
                seeded.Trip.OperatorId,
                seeded.Trip.DriverUserId);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task DestinationArrivalReplay_ReturnsStableResponseAndDoesNotDuplicateOutbox()
    {
        var databaseName = $"vietride_trip_destination_arrival_replay_{Guid.NewGuid():N}";
        await using var setup = CreateDbContext(databaseName);
        try
        {
            await setup.Database.MigrateAsync();
            var seeded = await SeedTripAsync(setup, includeStop: false);
            using var factory = new ArrivalWebApplicationFactory(new ArrivalDatabaseMediator(databaseName));
            using var client = factory.CreateClient();
            var path = $"/v1/driver/trips/{seeded.Trip.Id}/destination/arrive";
            var key = NewKey();

            var first = await client.SendAsync(
                CreateRequest(path, "DRIVER", seeded.Trip.DriverUserId, key));
            var firstBytes = await first.Content.ReadAsByteArrayAsync();
            var replay = await client.SendAsync(
                CreateRequest(path, "DRIVER", seeded.Trip.DriverUserId, key));

            first.StatusCode.Should().Be(HttpStatusCode.OK);
            replay.StatusCode.Should().Be(HttpStatusCode.OK);
            (await replay.Content.ReadAsByteArrayAsync()).Should().Equal(firstBytes);
            await using var assertionDb = CreateDbContext(databaseName);
            (await assertionDb.OutboxEvents.CountAsync(item =>
                item.EventType == "trip.destination.arrived")).Should().Be(1);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task RemovedOperatorStopArrivalRoute_Returns404WithoutDispatching()
    {
        using var factory = new ArrivalWebApplicationFactory(new RejectingMediator());
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            $"/v1/operator/trips/{Guid.NewGuid()}/stops/{Guid.NewGuid()}/arrive",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task AssertOneWinnerOneConflictAsync(
        HttpResponseMessage[] responses,
        string conflictCode)
    {
        responses.Count(response => response.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
        var conflict = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
        using var document = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be(conflictCode);
    }

    private static void AssertStopArrivedPayload(
        string payloadJson,
        Guid tripId,
        Guid stopId,
        Guid operatorId,
        Guid actorUserId)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var payload = document.RootElement;
        payload.GetProperty("eventId").GetGuid().Should().NotBeEmpty();
        payload.GetProperty("occurredAt").GetDateTime().Should().Be(Now.UtcDateTime);
        payload.GetProperty("eventType").GetString().Should().Be("trip.stop.arrived");
        payload.GetProperty("tripId").GetGuid().Should().Be(tripId);
        payload.GetProperty("stopId").GetGuid().Should().Be(stopId);
        payload.GetProperty("operatorId").GetGuid().Should().Be(operatorId);
        payload.GetProperty("actorUserId").GetGuid().Should().Be(actorUserId);
        payload.GetProperty("actualArrivalTime").GetDateTimeOffset().Should().Be(Now);
    }

    private static void AssertDestinationArrivedPayload(
        string payloadJson,
        Guid tripId,
        Guid destinationStationId,
        Guid operatorId,
        Guid actorUserId)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var payload = document.RootElement;
        payload.GetProperty("eventId").GetGuid().Should().NotBeEmpty();
        payload.GetProperty("occurredAt").GetDateTime().Should().Be(Now.UtcDateTime);
        payload.GetProperty("eventType").GetString().Should().Be("trip.destination.arrived");
        payload.GetProperty("tripId").GetGuid().Should().Be(tripId);
        payload.GetProperty("destinationStationId").GetGuid().Should().Be(destinationStationId);
        payload.GetProperty("operatorId").GetGuid().Should().Be(operatorId);
        payload.GetProperty("actorUserId").GetGuid().Should().Be(actorUserId);
        payload.GetProperty("actualArrivalTime").GetDateTimeOffset().Should().Be(Now);
    }

    private static HttpRequestMessage CreateRequest(
        string path,
        string role,
        Guid subject,
        string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.TryAddWithoutValidation(
            "X-Internal-Auth",
            $"Bearer {CreateInternalJwt(role, subject)}");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static string CreateInternalJwt(string role, Guid subject)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims: [new Claim("sub", subject.ToString()), new Claim(ClaimTypes.Role, role)],
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string NewKey() => Guid.NewGuid().ToString("D");

    private static TRepository CreateRepository<TRepository>(TripDbContext db, string typeName)
    {
        var type = typeof(TripDbContext).Assembly.GetType(typeName, throwOnError: true)!;
        return (TRepository)Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [db],
            culture: null)!;
    }

    private static async Task<SeededTrip> SeedTripAsync(TripDbContext db, bool includeStop)
    {
        var operatorId = Guid.NewGuid();
        var origin = Station.Create(
            "Arrival Origin",
            $"arrival-origin-{Guid.NewGuid():N}",
            "Ho Chi Minh City",
            "Ho Chi Minh City",
            latitude: 10.7769m,
            longitude: 106.7009m);
        var destination = Station.Create(
            "Arrival Destination",
            $"arrival-destination-{Guid.NewGuid():N}",
            "Da Lat",
            "Lam Dong",
            latitude: 11.9404m,
            longitude: 108.4583m);
        var route = VietRide.Trip.Domain.Entities.Route.Create(
            operatorId,
            "Arrival route",
            origin.Id,
            destination.Id,
            Money.FromRaw(100_000),
            300m,
            240);
        var vehicleType = VehicleType.Create(
            $"ARR_{Guid.NewGuid():N}"[..24],
            "Arrival test vehicle",
            5,
            20);
        using var layout = JsonDocument.Parse("{\"rows\":[]}");
        var vehicle = Vehicle.Create(
            operatorId,
            vehicleType.Id,
            $"ARR-{Guid.NewGuid():N}"[..20],
            layout.RootElement,
            20,
            500m,
            10m);
        var trip = VietRide.Trip.Domain.Entities.Trip.Create(
            operatorId,
            route.Id,
            vehicle.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            Now.AddHours(-2),
            Now.AddHours(2),
            TripSource.MANUAL,
            Money.FromRaw(100_000),
            500m,
            maxCargoVolumeM3: null,
            estimatedPassengerLuggageKg: 5m,
            seatLayoutSnapshotJson: vehicle.SeatLayoutJson);
        trip.MarkBoarding(Now.AddHours(-2));
        trip.Start(Now.AddHours(-2));

        TripStop? tripStop = null;
        if (includeStop)
        {
            var stop = Stop.Create(
                operatorId,
                "Arrival stop",
                10.9m,
                107.1m);
            tripStop = TripStop.Create(
                trip.Id,
                stop.Id,
                1,
                Now.AddMinutes(30),
                allowPickup: true,
                allowDropoff: true,
                distanceFromOriginKm: 50m);
            db.Add(stop);
            db.Add(tripStop);
        }

        db.AddRange(origin, destination, route, vehicleType, vehicle, trip);
        await db.SaveChangesAsync();
        return new SeededTrip(trip, route, tripStop);
    }

    private static TripDbContext CreateDbContext(string databaseName)
    {
        var builder = new NpgsqlDataSourceBuilder(CreateConnectionString(databaseName));
        builder.MapEnum<OutboxEventStatus>(
            $"{TripDbContext.SchemaName}.outbox_event_status",
            new NpgsqlNullNameTranslator());
        TripDbContext.ConfigurePostgresEnums(builder);
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .UseNpgsql(builder.Build(), npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", TripDbContext.SchemaName))
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new TripDbContext(options, new SystemClock());
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback = "Host=127.0.0.1;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var template = Environment.GetEnvironmentVariable("VIETRIDE_TRIP_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(template))
        {
            template = fallback;
        }

        return template.Contains("{databaseName}", StringComparison.OrdinalIgnoreCase)
            ? template.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase)
            : template;
    }

    private sealed record SeededTrip(
        VietRide.Trip.Domain.Entities.Trip Trip,
        VietRide.Trip.Domain.Entities.Route Route,
        TripStop? TripStop);

    private sealed class ArrivalWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly IMediator mediator;

        public ArrivalWebApplicationFactory(IMediator mediator)
        {
            this.mediator = mediator;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("INTERNAL_JWT_SECRET", TestSecret);
            builder.UseSetting("Trip:BackgroundWorkers:Enabled", "false");
            builder.UseSetting(
                "ConnectionStrings:Default",
                global::VietRide.Trip.IntegrationTests.VietRideWebApplicationFactory.ResolveConnectionString("postgres"));
            builder.UseSetting("REDIS_URL", "127.0.0.1:6379");
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMediator>();
                services.AddSingleton(mediator);
            });
        }
    }

    private sealed class ArrivalDatabaseMediator : IMediator
    {
        private readonly string databaseName;

        public ArrivalDatabaseMediator(string databaseName)
        {
            this.databaseName = databaseName;
        }

        public async Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            await using var db = CreateDbContext(databaseName);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var clock = new FrozenClock(Now);
            try
            {
                var trips = CreateRepository<ITripRepository>(
                    db,
                    "VietRide.Trip.Infrastructure.Persistence.Repositories.TripRepository");
                var outbox = new IntegrationEventOutbox(new OutboxStore(db, clock));
                object response = request switch
                {
                    ArriveTripStopCommand command => await new ArriveTripStopCommandHandler(
                        trips,
                        CreateRepository<ITripStopRepository>(
                            db,
                            "VietRide.Trip.Infrastructure.Persistence.Repositories.TripStopRepository"),
                        outbox,
                        clock).Handle(command, cancellationToken),
                    ArriveTripDestinationCommand command => await new ArriveTripDestinationCommandHandler(
                        trips,
                        CreateRepository<IRouteRepository>(
                            db,
                            "VietRide.Trip.Infrastructure.Persistence.Repositories.RouteRepository"),
                        outbox,
                        clock).Handle(command, cancellationToken),
                    _ => throw new InvalidOperationException($"Unexpected request {request.GetType().Name}."),
                };

                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return (TResponse)response;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            => Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default)
            => Empty<object?>();
    }

    private sealed class RejectingMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Removed route must not dispatch.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Removed route must not dispatch.");

        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            => Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default)
            => Empty<object?>();
    }

    private static async IAsyncEnumerable<T> Empty<T>()
    {
        await Task.CompletedTask;
        yield break;
    }

    private sealed class FrozenClock : IClock
    {
        public FrozenClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
