using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.IntegrationTests.Trips.Operations;

[Collection("Day24DepartStop")]
public sealed class Day24DepartStopWarningIntegrationTests
    : IClassFixture<Day24DepartStopWebApplicationFactory>
{
    private readonly Day24DepartStopWebApplicationFactory factory;

    public Day24DepartStopWarningIntegrationTests(
        Day24DepartStopWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Depart_PositiveCount_ReturnsEnvelopePersistsTimestampAndPendingOutboxIdentity()
    {
        var seeded = await SeedAsync();
        factory.Booking.Reset(2);
        using var client = factory.CreateClient();
        var key = Guid.NewGuid().ToString("D");

        using var first = await SendAsync(client, seeded, key);
        var firstBody = await first.Content.ReadAsStringAsync();
        using var replay = await SendAsync(client, seeded, key);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadAsStringAsync()).Should().Be(firstBody);
        using var document = JsonDocument.Parse(firstBody);
        var root = document.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("statusCode").GetInt32().Should().Be(200);
        root.GetProperty("data").EnumerateObject().Select(property => property.Name).Should().Equal(
            "tripId", "stopId", "departedAt", "pendingPassengerCount", "eventEmitted");
        root.GetProperty("data").GetProperty("pendingPassengerCount").GetInt32().Should().Be(2);
        root.GetProperty("data").GetProperty("eventEmitted").GetBoolean().Should().BeTrue();
        factory.Booking.Calls.Should().Be(1);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        var persistedStop = await db.TripStops.AsNoTracking().SingleAsync(
            row => row.TripId == seeded.Trip.Id && row.StopId == seeded.Stop.Id);
        persistedStop.ActualDepartureTime.Should().NotBeNull();
        var events = await db.OutboxEvents.AsNoTracking()
            .Where(row => row.EventType == "trip.stop.departed_with_pending")
            .ToListAsync();
        var matching = events.Where(row =>
            JsonDocument.Parse(row.Payload).RootElement.GetProperty("tripId").GetGuid()
                == seeded.Trip.Id).ToList();
        matching.Should().ContainSingle();
        matching[0].Status.Should().Be(OutboxEventStatus.PENDING);
        matching[0].PublishedAt.Should().BeNull();
        using var payload = JsonDocument.Parse(matching[0].Payload);
        payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(matching[0].Id);
    }

    [Fact]
    public async Task Depart_ZeroCountPersistsWithoutEvent_AndNewKeyReturnsAlreadyDeparted()
    {
        var seeded = await SeedAsync();
        factory.Booking.Reset(0);
        using var client = factory.CreateClient();

        using var first = await SendAsync(client, seeded, Guid.NewGuid().ToString("D"));
        using var second = await SendAsync(client, seeded, Guid.NewGuid().ToString("D"));

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        using var firstDocument = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        firstDocument.RootElement.GetProperty("data").GetProperty("eventEmitted")
            .GetBoolean().Should().BeFalse();
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertErrorCodeAsync(second, "TRIP_STOP_ALREADY_DEPARTED");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        var events = await db.OutboxEvents.AsNoTracking()
            .Where(row => row.EventType == "trip.stop.departed_with_pending")
            .ToListAsync();
        events.Should().NotContain(row =>
            JsonDocument.Parse(row.Payload, new JsonDocumentOptions()).RootElement.GetProperty("tripId").GetGuid()
                == seeded.Trip.Id);
    }

    [Fact]
    public async Task Depart_UpstreamFailureReturns502AndRollsBackTimestampAndEvent()
    {
        var seeded = await SeedAsync();
        factory.Booking.Reset(0, new HttpRequestException("Booking unavailable"));
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, seeded, Guid.NewGuid().ToString("D"));

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        await AssertErrorCodeAsync(response, "UPSTREAM_UNAVAILABLE");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        var persistedStop = await db.TripStops.AsNoTracking().SingleAsync(
            row => row.TripId == seeded.Trip.Id && row.StopId == seeded.Stop.Id);
        persistedStop.ActualDepartureTime.Should().BeNull();
        var events = await db.OutboxEvents.AsNoTracking()
            .Where(row => row.EventType == "trip.stop.departed_with_pending")
            .ToListAsync();
        events.Should().NotContain(row =>
            JsonDocument.Parse(row.Payload, new JsonDocumentOptions()).RootElement.GetProperty("tripId").GetGuid()
                == seeded.Trip.Id);
    }

    [Fact]
    public async Task Depart_EnforcesIdempotencyFingerprintBodylessAuthCrewAndTenant()
    {
        var first = await SeedAsync();
        var second = await SeedAsync();
        factory.Booking.Reset(0);
        using var client = factory.CreateClient();
        var key = Guid.NewGuid().ToString("D");

        using var success = await SendAsync(client, first, key);
        using var mismatch = await SendAsync(client, second, key);
        using var missingKey = await SendAsync(client, second, null);
        using var withBody = await SendAsync(
            client,
            second,
            Guid.NewGuid().ToString("D"),
            content: new StringContent("{}", Encoding.UTF8, "application/json"));
        using var wrongCrew = await SendAsync(
            client,
            second,
            Guid.NewGuid().ToString("D"),
            actorId: Guid.NewGuid());
        using var wrongTenant = await SendAsync(
            client,
            second,
            Guid.NewGuid().ToString("D"),
            operatorId: Guid.NewGuid());

        success.StatusCode.Should().Be(HttpStatusCode.OK);
        mismatch.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCodeAsync(mismatch, "IDEMPOTENCY_KEY_MISMATCH");
        missingKey.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCodeAsync(missingKey, "IDEMPOTENCY_KEY_REQUIRED");
        withBody.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        wrongCrew.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertErrorCodeAsync(wrongCrew, "FORBIDDEN");
        wrongTenant.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertErrorCodeAsync(wrongTenant, "FORBIDDEN");
    }

    [Fact]
    public async Task Depart_TwoConcurrentNewKeysHaveOneWinnerAndOneConflict()
    {
        var seeded = await SeedAsync();
        factory.Booking.Reset(1, delay: TimeSpan.FromMilliseconds(150));
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();

        var responses = await Task.WhenAll(
            SendAsync(firstClient, seeded, Guid.NewGuid().ToString("D")),
            SendAsync(secondClient, seeded, Guid.NewGuid().ToString("D")));

        responses.Select(response => response.StatusCode).Should().BeEquivalentTo(
            [HttpStatusCode.OK, HttpStatusCode.Conflict]);
        factory.Booking.Calls.Should().Be(1);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        var events = await db.OutboxEvents.AsNoTracking()
            .Where(row => row.EventType == "trip.stop.departed_with_pending")
            .ToListAsync();
        events.Count(row =>
            JsonDocument.Parse(row.Payload, new JsonDocumentOptions())
                .RootElement.GetProperty("tripId").GetGuid() == seeded.Trip.Id).Should().Be(1);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Depart_AllZeroRouteIdsReturnValidationEnvelope()
    {
        var seeded = await SeedAsync();
        using var client = factory.CreateClient();
        using var zeroTrip = await SendPathAsync(
            client,
            $"/v1/driver/trips/{Guid.Empty:D}/stops/{seeded.Stop.Id:D}/depart",
            seeded,
            Guid.NewGuid().ToString("D"));
        using var zeroStop = await SendPathAsync(
            client,
            $"/v1/driver/trips/{seeded.Trip.Id:D}/stops/{Guid.Empty:D}/depart",
            seeded,
            Guid.NewGuid().ToString("D"));

        zeroTrip.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCodeAsync(zeroTrip, "VALIDATION_ERROR");
        zeroStop.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCodeAsync(zeroStop, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Depart_MalformedRouteIdsReturnValidationEnvelopeInsteadOfRouting404()
    {
        var seeded = await SeedAsync();
        using var client = factory.CreateClient();
        using var malformedTrip = await SendPathAsync(
            client,
            $"/v1/driver/trips/not-a-uuid/stops/{seeded.Stop.Id:D}/depart",
            seeded,
            Guid.NewGuid().ToString("D"));
        using var malformedStop = await SendPathAsync(
            client,
            $"/v1/driver/trips/{seeded.Trip.Id:D}/stops/not-a-uuid/depart",
            seeded,
            Guid.NewGuid().ToString("D"));

        malformedTrip.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCodeAsync(malformedTrip, "VALIDATION_ERROR");
        malformedStop.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCodeAsync(malformedStop, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Depart_RequiresValidJwtAndDriverOrAssistantRole()
    {
        var seeded = await SeedAsync();
        using var client = factory.CreateClient();
        using var missing = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/driver/trips/{seeded.Trip.Id:D}/stops/{seeded.Stop.Id:D}/depart");
        missing.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var missingResponse = await client.SendAsync(missing);
        using var invalid = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/driver/trips/{seeded.Trip.Id:D}/stops/{seeded.Stop.Id:D}/depart");
        invalid.Headers.TryAddWithoutValidation("X-Internal-Auth", "Bearer invalid-token");
        invalid.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var invalidResponse = await client.SendAsync(invalid);
        using var wrongRole = await SendAsync(
            client,
            seeded,
            Guid.NewGuid().ToString("D"),
            role: "PASSENGER");

        missingResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertErrorCodeAsync(missingResponse, "AUTH_TOKEN_INVALID");
        invalidResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertErrorCodeAsync(invalidResponse, "AUTH_TOKEN_INVALID");
        wrongRole.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertErrorCodeAsync(wrongRole, "FORBIDDEN");
    }

    [Fact]
    public async Task Depart_AssignedAssistantInSameTenantCanSucceed()
    {
        var seeded = await SeedAsync();
        factory.Booking.Reset(0);
        using var client = factory.CreateClient();

        using var response = await SendAsync(
            client,
            seeded,
            Guid.NewGuid().ToString("D"),
            actorId: seeded.Trip.AssistantUserId!.Value,
            role: "ASSISTANT");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<SeededDeparture> SeedAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        await db.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var operatorId = Guid.NewGuid();
        var origin = Station.Create(
            "Day 24 departure origin",
            $"day24-origin-{Guid.NewGuid():N}",
            "Ho Chi Minh City",
            "Ho Chi Minh City");
        var destination = Station.Create(
            "Day 24 departure destination",
            $"day24-destination-{Guid.NewGuid():N}",
            "Da Nang",
            "Da Nang");
        var route = VietRide.Trip.Domain.Entities.Route.Create(
            operatorId,
            "Day 24 departure route",
            origin.Id,
            destination.Id,
            Money.FromRaw(100_000),
            100,
            240);
        var vehicleType = VehicleType.Create(
            $"D24_{Guid.NewGuid():N}",
            "Day 24 vehicle",
            null,
            1);
        var vehicle = Vehicle.Create(
            operatorId,
            vehicleType.Id,
            $"D24-{Guid.NewGuid():N}"[..20],
            JsonSerializer.SerializeToElement(new
            {
                version = 1,
                vehicleTypeCode = "D24",
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
            }),
            1,
            null,
            null);
        var trip = TripEntity.Create(
            operatorId,
            route.Id,
            vehicle.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            now.AddHours(-2),
            now.AddHours(2),
            TripSource.AUTO_FROM_SCHEDULE,
            Money.FromRaw(100_000),
            null,
            0);
        trip.MarkBoarding(now.AddHours(-2));
        trip.Start(now.AddHours(-1));
        var stop = Stop.Create(operatorId, $"Day24 {Guid.NewGuid():N}", 10, 106);
        var tripStop = TripStop.Create(trip.Id, stop.Id, 1, now, true, true, 5);
        tripStop.MarkArrived(now.AddMinutes(-5));
        db.AddRange(origin, destination, route, vehicleType, vehicle, trip, stop, tripStop);
        await db.SaveChangesAsync();
        return new SeededDeparture(trip, stop);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        SeededDeparture seeded,
        string? key,
        HttpContent? content = null,
        Guid? actorId = null,
        Guid? operatorId = null,
        string role = "DRIVER")
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/driver/trips/{seeded.Trip.Id:D}/stops/{seeded.Stop.Id:D}/depart")
        {
            Content = content,
        };
        request.Headers.TryAddWithoutValidation(
            "X-Internal-Auth",
            $"Bearer {CreateJwt(actorId ?? seeded.Trip.DriverUserId, operatorId ?? seeded.Trip.OperatorId, role)}");
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendPathAsync(
        HttpClient client,
        string path,
        SeededDeparture seeded,
        string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.TryAddWithoutValidation(
            "X-Internal-Auth",
            $"Bearer {CreateJwt(seeded.Trip.DriverUserId, seeded.Trip.OperatorId, "DRIVER")}");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static string CreateJwt(Guid actorId, Guid operatorId, string role)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("test-secret-at-least-32-characters-long"));
        var token = new JwtSecurityToken(
            "vietride-gateway",
            "vietride-internal",
            [
                new Claim("sub", actorId.ToString("D")),
                new Claim(ClaimTypes.Role, role),
                new Claim("operatorId", operatorId.ToString("D")),
            ],
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task AssertErrorCodeAsync(
        HttpResponseMessage response,
        string expectedCode)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be(expectedCode);
    }

    private sealed record SeededDeparture(TripEntity Trip, Stop Stop);
}

[CollectionDefinition("Day24DepartStop", DisableParallelization = true)]
public sealed class Day24DepartStopCollection;

public sealed class Day24DepartStopWebApplicationFactory : WebApplicationFactory<Program>
{
    public FakeBookingImpactClient Booking { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(
            "INTERNAL_JWT_SECRET",
            "test-secret-at-least-32-characters-long");
        builder.UseSetting("Trip:BackgroundWorkers:Enabled", "false");
        builder.UseSetting(
            "ConnectionStrings:Default",
            VietRideWebApplicationFactory.ResolveConnectionString("postgres"));
        builder.UseSetting("REDIS_URL", "127.0.0.1:6379");
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TripDbContext>();
            services.RemoveAll<DbContextOptions<TripDbContext>>();
            services.AddDbContext<TripDbContext>((serviceProvider, options) =>
                options
                    .UseNpgsql(
                        serviceProvider.GetRequiredService<NpgsqlDataSource>(),
                        npgsql => npgsql.MigrationsHistoryTable(
                            "__ef_migrations_history",
                            TripDbContext.SchemaName))
                    .ConfigureWarnings(warnings =>
                        warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));
            services.RemoveAll<IBookingImpactClient>();
            services.AddSingleton<IBookingImpactClient>(Booking);
        });
    }
}

public sealed class FakeBookingImpactClient : IBookingImpactClient
{
    private int calls;
    private int count;
    private Exception? exception;
    private TimeSpan delay;

    public int Calls => Volatile.Read(ref calls);

    public void Reset(int pendingCount, Exception? failure = null, TimeSpan? delay = null)
    {
        Volatile.Write(ref calls, 0);
        count = pendingCount;
        exception = failure;
        this.delay = delay ?? TimeSpan.Zero;
    }

    public async Task<TripStopPendingPassengerCountProjection> GetPendingPassengerCountAsync(
        Guid tripId,
        Guid stopId,
        Guid operatorId,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls);
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }

        if (exception is not null)
        {
            throw exception;
        }

        return new TripStopPendingPassengerCountProjection(tripId, stopId, count);
    }

    public Task<TripBookingImpactProjection> GetTripEditImpactAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();
}
