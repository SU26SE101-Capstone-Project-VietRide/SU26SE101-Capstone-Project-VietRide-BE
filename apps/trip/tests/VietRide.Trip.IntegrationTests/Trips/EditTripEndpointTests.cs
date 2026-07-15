using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Web.Idempotency;
using VietRide.Trip.Api.Controllers;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Trips.EditTrip;
using VietRide.Trip.Application.Features.Trips.GetTripDetail;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests.Trips;

public sealed class EditTripEndpointTests
{
    private const string TestSecret = "test-secret-at-least-32-characters-long";

    [Fact]
    public void Endpoint_UsesSharedBodyAwareIdempotencyMetadata()
    {
        var method = typeof(OperatorTripsController).GetMethod(nameof(OperatorTripsController.EditAsync))!;

        method.GetCustomAttributes(typeof(HttpPatchAttribute), false).Cast<HttpPatchAttribute>()
            .Single().Template.Should().Be("{tripId:guid}");
        method.GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>()
            .Single().Roles.Should().Be("OPERATOR_ADMIN");
        var metadata = method.GetCustomAttributes(typeof(RequireIdempotencyAttribute), false)
            .Cast<RequireIdempotencyAttribute>()
            .Single();
        metadata.AllowRequestBody.Should().BeTrue();
    }

    [Fact]
    public async Task Patch_RequiresUuidV4_AndDoesNotDispatchInvalidKey()
    {
        var mediator = new StubMediator(_ => CreateDetail(notes: null));
        using var factory = new EditTripWebApplicationFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateRequest(Guid.NewGuid(), Guid.NewGuid(), "not-a-uuid-v4", "{\"notes\":\"dispatch\"}");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        mediator.SendCount.Should().Be(0);
        await AssertErrorCodeAsync(response, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Patch_ReplaysSameBody_AndRejectsChangedBodyForSameKey()
    {
        var mediator = new StubMediator(request =>
        {
            var command = request.Should().BeOfType<EditTripCommand>().Subject;
            return CreateDetail(command.Notes);
        });
        using var factory = new EditTripWebApplicationFactory(mediator);
        using var client = factory.CreateClient();
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var key = Guid.NewGuid().ToString("D");

        using var first = await client.SendAsync(CreateRequest(tripId, operatorId, key, "{\"notes\":\"first\"}"));
        using var replay = await client.SendAsync(CreateRequest(tripId, operatorId, key, "{\"notes\":\"first\"}"));
        using var mismatch = await client.SendAsync(CreateRequest(tripId, operatorId, key, "{\"notes\":\"second\"}"));

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await first.Content.ReadAsStringAsync()).Should().Be(await replay.Content.ReadAsStringAsync());
        mediator.SendCount.Should().Be(1);
        mismatch.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCodeAsync(mismatch, "IDEMPOTENCY_KEY_MISMATCH");
    }

    [Fact]
    public async Task Patch_TenantScopeIsPassedToHandler_AndMaskedNotFoundIsPreserved()
    {
        var owningOperator = Guid.NewGuid();
        var mediator = new StubMediator(request =>
        {
            var command = request.Should().BeOfType<EditTripCommand>().Subject;
            if (command.OperatorId != owningOperator)
            {
                throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
            }

            return CreateDetail(command.Notes);
        });
        using var factory = new EditTripWebApplicationFactory(mediator);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(CreateRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D"),
            "{\"baseFare\":450001,\"notes\":\" combined \"}"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertErrorCodeAsync(response, "TRIP_NOT_FOUND");
        mediator.SendCount.Should().Be(1);
    }

    [Theory]
    [InlineData("{\"departureDateTime\":\"2026-07-20T09:00:00Z\"}")]
    [InlineData("{\"driverUserId\":\"11111111-1111-4111-8111-111111111111\"}")]
    [InlineData("{\"assistantUserId\":\"11111111-1111-4111-8111-111111111111\"}")]
    [InlineData("{\"crew\":{}}")]
    public async Task Patch_RejectsDepartureAndCrewFields(string body)
    {
        var mediator = new StubMediator(_ => CreateDetail(notes: null));
        using var factory = new EditTripWebApplicationFactory(mediator);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(CreateRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D"),
            body));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        mediator.SendCount.Should().Be(0);
        await AssertErrorCodeAsync(response, "VALIDATION_ERROR");
    }

    [Theory]
    [InlineData("{\"baseFare\":null}")]
    [InlineData("{\"vehicleId\":null}")]
    [InlineData("{\"routeId\":null}")]
    public async Task Patch_ExplicitNullForRequiredEditField_ReturnsValidationError(string body)
    {
        var mediator = new StubMediator(request =>
        {
            var command = request.Should().BeOfType<EditTripCommand>().Subject;
            var validation = new EditTripValidator().Validate(command);
            if (!validation.IsValid)
            {
                throw new CodedValidationException("VALIDATION_ERROR", "Invalid Trip edit.");
            }

            return CreateDetail(command.Notes);
        });
        using var factory = new EditTripWebApplicationFactory(mediator);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(CreateRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D"),
            body));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCodeAsync(response, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Patch_ReturnsTrimmedNotesInTripDetail()
    {
        var mediator = new StubMediator(request =>
        {
            var command = request.Should().BeOfType<EditTripCommand>().Subject;
            return CreateDetail(command.Notes);
        });
        using var factory = new EditTripWebApplicationFactory(mediator);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(CreateRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D"),
            "{\"notes\":\"  dispatch note  \"}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("data").GetProperty("notes").GetString()
            .Should().Be("dispatch note");
    }

    [Fact]
    public async Task VehicleConflictCheck_InsideTransaction_SerializesBeforeQueryingCommittedState()
    {
        var databaseName = $"vietride_trip_edit_conflict_{Guid.NewGuid():N}";
        VehicleConflictSeed seed;
        await using (var setupDb = CreateTripDbContext(databaseName))
        {
            await setupDb.Database.MigrateAsync();
            seed = await SeedVehicleConflictAsync(setupDb);
        }

        try
        {
            await using var firstDb = CreateTripDbContext(databaseName);
            await using var secondDb = CreateTripDbContext(databaseName);
            var firstRepository = CreateTripRepository(firstDb);
            var secondRepository = CreateTripRepository(secondDb);
            await using var firstTransaction = await firstDb.Database.BeginTransactionAsync();

            var initialConflict = await firstRepository.HasVehicleConflictAsync(
                seed.TargetVehicleId,
                seed.Departure,
                seed.FirstTripId,
                CancellationToken.None);
            initialConflict.Should().BeFalse();

            await firstDb.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE vietride_trip.trips
                SET vehicle_id = {seed.TargetVehicleId}
                WHERE id = {seed.FirstTripId}
                """);

            var secondCheckStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondCheck = Task.Run(async () =>
            {
                await using var secondTransaction = await secondDb.Database.BeginTransactionAsync();
                secondCheckStarted.SetResult();
                var conflict = await secondRepository.HasVehicleConflictAsync(
                    seed.TargetVehicleId,
                    seed.Departure.ToOffset(TimeSpan.FromHours(7)),
                    seed.SecondTripId,
                    CancellationToken.None);
                await secondTransaction.RollbackAsync();
                return conflict;
            });

            await secondCheckStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(150);
            secondCheck.IsCompleted.Should().BeFalse(
                "the second transaction must wait for the first transaction's vehicle/departure advisory lock");

            await firstTransaction.CommitAsync();

            (await secondCheck.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue(
                "the conflict query must run after the first transaction commits its vehicle assignment");
        }
        finally
        {
            await using var cleanupDb = CreateTripDbContext(databaseName);
            await cleanupDb.Database.EnsureDeletedAsync();
        }
    }

    private static HttpRequestMessage CreateRequest(Guid tripId, Guid operatorId, string key, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/v1/operator/trips/{tripId}");
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt(operatorId)}");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return request;
    }

    private static TripDbContext CreateTripDbContext(string databaseName)
    {
        var connectionString = $"Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", TripDbContext.SchemaName))
            .Options;
        return new TripDbContext(options, new SystemClock());
    }

    private static ITripRepository CreateTripRepository(TripDbContext dbContext)
    {
        var repositoryType = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Persistence.Repositories.TripRepository",
            throwOnError: true)!;
        return (ITripRepository)Activator.CreateInstance(repositoryType, dbContext)!;
    }

    private static async Task<VehicleConflictSeed> SeedVehicleConflictAsync(TripDbContext dbContext)
    {
        var operatorId = Guid.NewGuid();
        var originId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        var vehicleTypeId = Guid.NewGuid();
        var firstVehicleId = Guid.NewGuid();
        var secondVehicleId = Guid.NewGuid();
        var targetVehicleId = Guid.NewGuid();
        var firstTripId = Guid.NewGuid();
        var secondTripId = Guid.NewGuid();
        var departure = new DateTimeOffset(2026, 7, 20, 1, 0, 0, TimeSpan.Zero);
        const string layout = """
            {"version":1,"vehicleTypeCode":"TEST","totalSeats":1,"rows":1,"cols":1,"decks":1,"aisles":[],"seats":[{"seatNumber":"A01","row":1,"col":1,"deck":1,"type":"STANDARD","isWindow":false,"isAisle":false,"disabled":false}]}
            """;

        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO vietride_trip.stations (id, name, slug, city, province)
            VALUES
                ({originId}, 'Conflict Origin', {$"conflict-origin-{originId:N}"}, 'HCMC', 'HCMC'),
                ({destinationId}, 'Conflict Destination', {$"conflict-destination-{destinationId:N}"}, 'Da Nang', 'Da Nang');
            INSERT INTO vietride_trip.routes
                (id, operator_id, name, origin_station_id, destination_station_id, base_fare, estimated_duration_minutes)
            VALUES
                ({routeId}, {operatorId}, 'Conflict Route', {originId}, {destinationId}, 100000, 240);
            INSERT INTO vietride_trip.vehicle_types (id, code, display_name, default_seat_count)
            VALUES ({vehicleTypeId}, {$"CONFLICT_{vehicleTypeId:N}"}, 'Conflict vehicle', 1);
            INSERT INTO vietride_trip.vehicles
                (id, operator_id, vehicle_type_id, license_plate, seat_layout_json, total_seats)
            VALUES
                ({firstVehicleId}, {operatorId}, {vehicleTypeId}, {$"A-{firstVehicleId:N}"[..20]}, CAST({layout} AS jsonb), 1),
                ({secondVehicleId}, {operatorId}, {vehicleTypeId}, {$"B-{secondVehicleId:N}"[..20]}, CAST({layout} AS jsonb), 1),
                ({targetVehicleId}, {operatorId}, {vehicleTypeId}, {$"C-{targetVehicleId:N}"[..20]}, CAST({layout} AS jsonb), 1);
            INSERT INTO vietride_trip.trips
                (id, operator_id, route_id, vehicle_id, driver_user_id, departure_date_time,
                 estimated_arrival_time, source, base_fare)
            VALUES
                ({firstTripId}, {operatorId}, {routeId}, {firstVehicleId}, {Guid.NewGuid()}, {departure},
                 {departure.AddHours(4)}, 'MANUAL', 100000),
                ({secondTripId}, {operatorId}, {routeId}, {secondVehicleId}, {Guid.NewGuid()}, {departure},
                 {departure.AddHours(4)}, 'MANUAL', 100000);
            """);

        return new VehicleConflictSeed(firstTripId, secondTripId, targetVehicleId, departure);
    }

    private static string CreateInternalJwt(Guid operatorId)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims: [
                new Claim("sub", operatorId.ToString()),
                new Claim(ClaimTypes.Role, "OPERATOR_ADMIN"),
                new Claim("operatorId", operatorId.ToString()),
            ],
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task AssertErrorCodeAsync(HttpResponseMessage response, string expected)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(expected);
    }

    private static TripDetailDto CreateDetail(string? notes) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "SCHEDULED",
        DateTimeOffset.Parse("2026-07-20T08:00:00+07:00"),
        DateTimeOffset.Parse("2026-07-20T12:00:00+07:00"),
        450_001,
        new TripStationDto(Guid.NewGuid(), "Origin"),
        new TripStationDto(Guid.NewGuid(), "Destination"),
        [],
        new TripSeatSummaryDto(40, 40),
        null,
        new TripFareBreakdownDto(450_001, []))
    { Notes = notes?.Trim() };

    private sealed class EditTripWebApplicationFactory(IMediator mediator) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Environment.SetEnvironmentVariable("INTERNAL_JWT_SECRET", TestSecret);
            builder.UseSetting("INTERNAL_JWT_SECRET", TestSecret);
            builder.UseSetting("Trip:BackgroundWorkers:Enabled", "false");
            builder.UseSetting("ConnectionStrings:Default", "Host=localhost;Port=5432;Database=test;Username=vietride;Password=vietride_dev");
            builder.UseSetting("REDIS_URL", "localhost:6379");
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMediator>();
                services.AddSingleton(mediator);
            });
        }
    }

    private sealed class StubMediator(Func<object, object?> responder) : IMediator
    {
        public int SendCount { get; private set; }
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            SendCount++;
            return Task.FromResult((TResponse)responder(request)!);
        }
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) { SendCount++; return Task.FromResult(responder(request)); }
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => Empty<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => Empty<object?>();
        private static async IAsyncEnumerable<T> Empty<T>() { await Task.CompletedTask; yield break; }
    }

    private sealed record VehicleConflictSeed(
        Guid FirstTripId,
        Guid SecondTripId,
        Guid TargetVehicleId,
        DateTimeOffset Departure);
}
