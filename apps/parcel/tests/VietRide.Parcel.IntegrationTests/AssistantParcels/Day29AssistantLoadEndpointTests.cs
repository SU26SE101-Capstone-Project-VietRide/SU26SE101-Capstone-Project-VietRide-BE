using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using StackExchange.Redis;
using VietRide.Parcel.Api.Controllers;
using VietRide.Parcel.Api.Controllers.Requests;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels.MarkLoaded;
using VietRide.Parcel.Domain.Enums;
using VietRide.Parcel.Infrastructure;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Web.Authentication;
using VietRide.Shared.Web.Idempotency;

namespace VietRide.Parcel.IntegrationTests.AssistantParcels;

public sealed class Day29AssistantLoadEndpointTests
    : IClassFixture<Day29AssistantLoadWebApplicationFactory>
{
    private readonly Day29AssistantLoadWebApplicationFactory factory;

    public Day29AssistantLoadEndpointTests(Day29AssistantLoadWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task SuccessfulLoad_CommitsTransitionAndPendingLoadedOutboxAtomically()
    {
        var fixture = await ResetAndSeedAsync(ParcelStatus.PENDING);
        using var client = factory.CreateAssistantClient(fixture.AssistantUserId, fixture.OperatorId);

        var response = await PostLoadAsync(client, fixture, Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertSuccessResponseAsync(response, fixture);
        await factory.AssertPersistedAsync(fixture, ParcelStatus.LOADED, expectedWrites: 1);
    }

    [Fact]
    public async Task DownstreamCargoFailure_RollsBackTransitionAndLoadedOutbox()
    {
        var fixture = await ResetAndSeedAsync(ParcelStatus.PENDING);
        factory.Scenario.CargoFailure = true;
        using var client = factory.CreateAssistantClient(fixture.AssistantUserId, fixture.OperatorId);

        var response = await PostLoadAsync(client, fixture, Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        await factory.AssertPersistedAsync(fixture, ParcelStatus.PENDING, expectedWrites: 0);
    }

    [Fact]
    public async Task CommitFailure_RollsBackTransitionAndLoadedOutbox()
    {
        var fixture = await ResetAndSeedAsync(ParcelStatus.PENDING);
        factory.Scenario.FailNextCommit = true;
        using var client = factory.CreateAssistantClient(fixture.AssistantUserId, fixture.OperatorId);

        var response = await PostLoadAsync(client, fixture, Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        await factory.AssertPersistedAsync(fixture, ParcelStatus.PENDING, expectedWrites: 0);
    }

    [Fact]
    public async Task MissingOrMalformedIdempotencyKey_IsRejectedWithoutWrites()
    {
        var fixture = await ResetAndSeedAsync(ParcelStatus.PENDING);
        using var client = factory.CreateAssistantClient(fixture.AssistantUserId, fixture.OperatorId);

        var missing = await client.PostAsJsonAsync(
            LoadPath(fixture.ParcelId),
            new { fixture.TripId, fixture.ParcelCode });
        using var malformedRequest = new HttpRequestMessage(HttpMethod.Post, LoadPath(fixture.ParcelId))
        {
            Content = JsonContent.Create(new { fixture.TripId, fixture.ParcelCode }),
        };
        malformedRequest.Headers.Add("Idempotency-Key", "not-a-uuid-v4");
        var malformed = await client.SendAsync(malformedRequest);

        missing.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadErrorCodeAsync(missing)).Should().Be("IDEMPOTENCY_KEY_REQUIRED");
        malformed.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadErrorCodeAsync(malformed)).Should().Be("VALIDATION_ERROR");
        await factory.AssertPersistedAsync(fixture, ParcelStatus.PENDING, expectedWrites: 0);
    }

    [Fact]
    public async Task NonAssistantIdentity_ReturnsForbiddenWithoutWrites()
    {
        var fixture = await ResetAndSeedAsync(ParcelStatus.PENDING);
        using var client = factory.CreateUserClient(
            "PASSENGER",
            fixture.AssistantUserId,
            fixture.OperatorId);

        var response = await PostLoadAsync(client, fixture, Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await factory.AssertPersistedAsync(fixture, ParcelStatus.PENDING, expectedWrites: 0);
    }

    [Fact]
    public async Task ForeignAssistantOrTenant_ReturnsForbiddenWithoutParcelDisclosure()
    {
        var fixture = await ResetAndSeedAsync(ParcelStatus.PENDING);
        using var foreignTenant = factory.CreateAssistantClient(
            fixture.AssistantUserId,
            Guid.NewGuid());

        var tenantResponse = await PostLoadAsync(foreignTenant, fixture, Guid.NewGuid());

        tenantResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadErrorCodeAsync(tenantResponse)).Should().Be("FORBIDDEN");

        factory.Scenario.AssistantDenied = true;
        using var unassigned = factory.CreateAssistantClient(
            Guid.NewGuid(),
            fixture.OperatorId);
        var crewResponse = await PostLoadAsync(unassigned, fixture, Guid.NewGuid());

        crewResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadErrorCodeAsync(crewResponse)).Should().Be("FORBIDDEN");
        await factory.AssertPersistedAsync(fixture, ParcelStatus.PENDING, expectedWrites: 0);
    }

    [Fact]
    public async Task HiddenTripOrParcelCodeMismatch_ReturnsParcelNotFoundWithoutWrites()
    {
        var fixture = await ResetAndSeedAsync(ParcelStatus.PENDING);
        using var client = factory.CreateAssistantClient(fixture.AssistantUserId, fixture.OperatorId);

        var wrongTrip = await PostLoadAsync(
            client,
            fixture with { TripId = Guid.NewGuid() },
            Guid.NewGuid());
        var wrongCode = await PostLoadAsync(
            client,
            fixture with { ParcelCode = "VRP-20260722-WRONGCODE" },
            Guid.NewGuid());

        wrongTrip.StatusCode.Should().Be(HttpStatusCode.NotFound);
        wrongCode.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadErrorCodeAsync(wrongTrip)).Should().Be("PARCEL_NOT_FOUND");
        (await ReadErrorCodeAsync(wrongCode)).Should().Be("PARCEL_NOT_FOUND");
        await factory.AssertPersistedAsync(fixture, ParcelStatus.PENDING, expectedWrites: 0);
    }

    [Fact]
    public async Task InvalidStatusOrConcurrentRaceLoser_ReturnsInvalidStatusWithoutWrites()
    {
        var fixture = await ResetAndSeedAsync(ParcelStatus.PENDING);
        using var firstClient = factory.CreateAssistantClient(fixture.AssistantUserId, fixture.OperatorId);
        using var secondClient = factory.CreateAssistantClient(fixture.AssistantUserId, fixture.OperatorId);

        var responses = await Task.WhenAll(
            PostLoadAsync(firstClient, fixture, Guid.NewGuid()),
            PostLoadAsync(secondClient, fixture, Guid.NewGuid()));

        responses.Should().ContainSingle(response => response.StatusCode == HttpStatusCode.OK);
        var loser = responses.Should().ContainSingle(
            response => response.StatusCode == HttpStatusCode.Conflict).Subject;
        (await ReadErrorCodeAsync(loser)).Should().Be("INVALID_STATUS");
        await factory.AssertPersistedAsync(fixture, ParcelStatus.LOADED, expectedWrites: 1);
    }

    [Fact]
    public async Task SameKeySamePayload_ReplaysOriginalResponseWithoutDuplicateWrites()
    {
        var fixture = await ResetAndSeedAsync(ParcelStatus.PENDING);
        var key = Guid.NewGuid();
        using var client = factory.CreateAssistantClient(fixture.AssistantUserId, fixture.OperatorId);

        var first = await PostLoadAsync(client, fixture, key);
        var firstBytes = await first.Content.ReadAsByteArrayAsync();
        var replay = await PostLoadAsync(client, fixture, key);
        var replayBytes = await replay.Content.ReadAsByteArrayAsync();

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        replayBytes.Should().Equal(firstBytes);
        factory.Scenario.LoadCargoCalls.Should().Be(1);
        await factory.AssertPersistedAsync(fixture, ParcelStatus.LOADED, expectedWrites: 1);
    }

    [Fact]
    public async Task SameKeyDifferentPayload_ReturnsIdempotencyKeyMismatchWithoutWrites()
    {
        var fixture = await ResetAndSeedAsync(ParcelStatus.PENDING);
        var key = Guid.NewGuid();
        using var client = factory.CreateAssistantClient(fixture.AssistantUserId, fixture.OperatorId);

        var first = await PostLoadAsync(client, fixture, key);
        var mismatch = await PostLoadAsync(
            client,
            fixture with { ParcelCode = "VRP-20260722-DIFFERENT" },
            key);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        mismatch.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadErrorCodeAsync(mismatch)).Should().Be("IDEMPOTENCY_KEY_MISMATCH");
        factory.Scenario.LoadCargoCalls.Should().Be(1);
        await factory.AssertPersistedAsync(fixture, ParcelStatus.LOADED, expectedWrites: 1);
    }

    [Fact]
    public async Task RemoteTripSuccessThenParcelCommitFailure_SameKeyRetryConvergesExactlyOnce()
    {
        var fixture = await ResetAndSeedAsync(ParcelStatus.PENDING);
        var key = Guid.NewGuid();
        factory.Scenario.FailNextCommit = true;
        using var client = factory.CreateAssistantClient(fixture.AssistantUserId, fixture.OperatorId);

        var failed = await PostLoadAsync(client, fixture, key);
        await factory.AssertPersistedAsync(fixture, ParcelStatus.PENDING, expectedWrites: 0);
        var retried = await PostLoadAsync(client, fixture, key);

        failed.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        retried.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Scenario.LoadCargoCalls.Should().Be(2);
        await factory.AssertPersistedAsync(fixture, ParcelStatus.LOADED, expectedWrites: 1);
    }

    [Fact]
    public void Endpoint_DeclaresSharedIdempotencyAndDocumentedApiResponseMetadata()
    {
        var method = typeof(AssistantParcelsController).GetMethod(
            nameof(AssistantParcelsController.LoadAsync),
            BindingFlags.Instance | BindingFlags.Public);

        method.Should().NotBeNull();
        var loadMethod = method!;
        loadMethod.GetCustomAttribute<RequireIdempotencyAttribute>().Should().NotBeNull();
        var responseTypes = loadMethod.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .ToDictionary(attribute => attribute.StatusCode);
        responseTypes.Keys.Should().BeEquivalentTo(new[] { 200, 401, 403, 404, 409, 422 });
        responseTypes[200].Type.Should().Be(typeof(ApiResponse<MarkParcelLoadedResponse>));
        foreach (var statusCode in new[] { 401, 403, 404, 409, 422 })
        {
            responseTypes[statusCode].Type.Should().Be(typeof(ApiResponse));
        }
        typeof(LoadParcelRequest).GetProperties()
            .Select(property => property.Name)
            .Should().BeEquivalentTo(nameof(LoadParcelRequest.TripId), nameof(LoadParcelRequest.ParcelCode));
    }

    private async Task<AssistantLoadFixture> ResetAndSeedAsync(ParcelStatus status)
    {
        await factory.InitializeDatabaseAsync();
        await factory.ResetAsync();
        return await factory.SeedParcelAsync(status);
    }

    private static async Task<HttpResponseMessage> PostLoadAsync(
        HttpClient client,
        AssistantLoadFixture fixture,
        Guid idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, LoadPath(fixture.ParcelId))
        {
            Content = JsonContent.Create(new { fixture.TripId, fixture.ParcelCode }),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey.ToString("D"));
        return await client.SendAsync(request);
    }

    private static string LoadPath(Guid parcelId)
        => $"/v1/assistant/parcels/{parcelId:D}/load";

    private static async Task AssertSuccessResponseAsync(
        HttpResponseMessage response,
        AssistantLoadFixture fixture)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("statusCode").GetInt32().Should().Be(200);
        var data = root.GetProperty("data");
        data.GetProperty("parcelId").GetGuid().Should().Be(fixture.ParcelId);
        data.GetProperty("parcelCode").GetString().Should().Be(fixture.ParcelCode);
        data.GetProperty("status").GetString().Should().Be("LOADED");
    }

    private static async Task<string> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("error").GetProperty("code").GetString()!;
    }
}

public sealed class Day29AssistantLoadWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string TestSecret = "test-secret-at-least-32-chars-long-xxxxx";
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private readonly string connectionString = BuildTestConnectionString();
    private bool databaseCreated;
    private bool initialized;

    public Day29AssistantLoadScenario Scenario { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("INTERNAL_JWT_SECRET", TestSecret);
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Trip:BaseUrl", "http://trip.invalid");
        builder.UseSetting("Payment:BaseUrl", "http://payment.invalid");
        builder.UseSetting("Booking:BaseUrl", "http://booking.invalid");
        builder.UseSetting("Identity:BaseUrl", "http://identity.invalid");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Trip:UseDevStub"] = "true",
                ["Payment:UseDevStub"] = "true",
                ["Booking:UseDevStub"] = "true",
                ["Identity:UseDevStub"] = "true",
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IConnectionMultiplexer>();
            services.AddSingleton(Scenario.Redis);
            services.RemoveAll<ITripServiceClient>();
            services.AddSingleton<ITripServiceClient>(Scenario.TripClient);
            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork>(provider => new ControlledUnitOfWork(
                provider.GetRequiredService<ParcelDbContext>(),
                Scenario));
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        if (initialized)
        {
            return;
        }

        await initializationLock.WaitAsync();
        try
        {
            if (initialized)
            {
                return;
            }

            await CreateDatabaseAsync();
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ParcelDbContext>();
            await db.Database.MigrateAsync();
            initialized = true;
        }
        finally
        {
            initializationLock.Release();
        }
    }

    public async Task ResetAsync()
    {
        Scenario.Reset();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ParcelDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE vietride_parcel.outbox_events, " +
            "vietride_parcel.parcel_stats, vietride_parcel.parcels CASCADE;");
    }

    public async Task<AssistantLoadFixture> SeedParcelAsync(ParcelStatus status)
    {
        var fixture = new AssistantLoadFixture(
            Guid.NewGuid(),
            $"VRP-20260722-{Guid.NewGuid():N}"[..24],
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ParcelDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO vietride_parcel.parcels (
                id, parcel_code, sender_user_id, recipient_user_id,
                recipient_name, recipient_phone, operator_id, trip_id,
                size_category, estimated_weight_kg, estimated_volume_m3,
                total_price_vnd, deposit_amount, original_deposit_amount, status)
            VALUES (
                {fixture.ParcelId}, {fixture.ParcelCode}, {fixture.SenderUserId},
                {fixture.RecipientUserId}, 'Recipient', '+84901234567',
                {fixture.OperatorId}, {fixture.TripId},
                'MEDIUM'::vietride_parcel.parcel_size_category, 12.5, 0.25,
                100000, 100000, 100000,
                CAST({status.ToString()} AS vietride_parcel.parcel_status));
            """);
        return fixture;
    }

    public async Task AssertPersistedAsync(
        AssistantLoadFixture fixture,
        ParcelStatus expectedStatus,
        int expectedWrites)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ParcelDbContext>();
        var parcel = await db.Parcels.AsNoTracking().SingleAsync(row => row.Id == fixture.ParcelId);
        parcel.Status.Should().Be(expectedStatus);
        var outboxRows = await db.OutboxEvents.AsNoTracking().ToListAsync();
        var statsRows = await db.ParcelStats.AsNoTracking().ToListAsync();
        outboxRows.Should().HaveCount(expectedWrites);
        statsRows.Should().HaveCount(expectedWrites);

        if (expectedWrites == 0)
        {
            parcel.LoadedAt.Should().BeNull();
            parcel.LoadedByUserId.Should().BeNull();
            return;
        }

        parcel.LoadedAt.Should().NotBeNull();
        parcel.LoadedByUserId.Should().Be(fixture.AssistantUserId);
        var loaded = outboxRows.Should().ContainSingle().Subject;
        loaded.EventType.Should().Be("parcel.parcel.loaded");
        loaded.Status.Should().Be(OutboxEventStatus.PENDING);
        loaded.PublishedAt.Should().BeNull();
        using var json = JsonDocument.Parse(loaded.Payload);
        var root = json.RootElement;
        root.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "eventId",
            "occurredAt",
            "parcelId",
            "tripId",
            "actualWeightKg",
            "userIds");
        root.GetProperty("eventId").GetGuid().Should().Be(loaded.Id);
        root.GetProperty("parcelId").GetGuid().Should().Be(fixture.ParcelId);
        root.GetProperty("tripId").GetGuid().Should().Be(fixture.TripId);
        root.GetProperty("occurredAt").GetDateTimeOffset().Should().NotBe(default);
        root.GetProperty("actualWeightKg").GetDecimal().Should().Be(12.5m);
        var userIds = root.GetProperty("userIds")
            .EnumerateArray()
            .Select(value => value.GetGuid())
            .ToArray();
        userIds.Should().OnlyHaveUniqueItems();
        userIds.Should().BeEquivalentTo(new[] { fixture.SenderUserId, fixture.RecipientUserId });
        statsRows.Should().ContainSingle(row => row.TotalLoaded == 1);
    }

    public HttpClient CreateAssistantClient(Guid userId, Guid operatorId)
        => CreateUserClient("ASSISTANT", userId, operatorId);

    public HttpClient CreateUserClient(string role, Guid userId, Guid operatorId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            InternalJwtAuthenticationExtensions.HeaderName,
            $"Bearer {MintJwt(role, userId, operatorId)}");
        return client;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (databaseCreated)
        {
            await DropDatabaseAsync();
        }

        initializationLock.Dispose();
    }

    private async Task CreateDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(MaintenanceConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{DatabaseName()}\";";
        await command.ExecuteNonQueryAsync();
        databaseCreated = true;
    }

    private async Task DropDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(MaintenanceConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{DatabaseName()}\" WITH (FORCE);";
        await command.ExecuteNonQueryAsync();
        databaseCreated = false;
    }

    private string DatabaseName()
        => new NpgsqlConnectionStringBuilder(connectionString).Database!;

    private string MaintenanceConnectionString()
        => new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ConnectionString;

    private static string BuildTestConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("VIETRIDE_PARCEL_TEST_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=unused;Username=vietride;Password=vietride_dev";
        return new NpgsqlConnectionStringBuilder(configured)
        {
            Database = $"vietride_parcel_day29_assistant_load_{Guid.NewGuid():N}",
        }.ConnectionString;
    }

    private static string MintJwt(string role, Guid userId, Guid operatorId)
    {
        var now = DateTime.UtcNow;
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString("D")),
                new Claim("role", role),
                new Claim("operatorId", operatorId.ToString("D")),
            ],
            notBefore: now.AddSeconds(-5),
            expires: now.AddMinutes(15),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public sealed record AssistantLoadFixture(
    Guid ParcelId,
    string ParcelCode,
    Guid TripId,
    Guid OperatorId,
    Guid AssistantUserId,
    Guid SenderUserId,
    Guid RecipientUserId)
{
    public AssistantLoadFixture(
        Guid parcelId,
        string parcelCode,
        Guid tripId,
        Guid operatorId,
        Guid assistantUserId,
        Guid senderUserId)
        : this(parcelId, parcelCode, tripId, operatorId, assistantUserId, senderUserId, Guid.NewGuid())
    {
    }
}

public sealed class Day29AssistantLoadScenario
{
    public Day29AssistantLoadScenario()
    {
        Redis = Day29RedisConnection.Create();
        TripClient = new ControlledTripServiceClient(this);
    }

    public IConnectionMultiplexer Redis { get; }
    public ITripServiceClient TripClient { get; }
    public bool CargoFailure { get; set; }
    public bool AssistantDenied { get; set; }
    public bool FailNextCommit { get; set; }
    public int LoadCargoCalls;

    public void Reset()
    {
        CargoFailure = false;
        AssistantDenied = false;
        FailNextCommit = false;
        LoadCargoCalls = 0;
        Day29RedisConnection.Reset();
    }

    public bool ConsumeCommitFailure()
    {
        if (!FailNextCommit)
        {
            return false;
        }

        FailNextCommit = false;
        return true;
    }
}

internal sealed class ControlledTripServiceClient : ITripServiceClient
{
    private readonly Day29AssistantLoadScenario scenario;

    public ControlledTripServiceClient(Day29AssistantLoadScenario scenario)
    {
        this.scenario = scenario;
    }

    public Task<TripCrewAuthorizationOutcome> AuthorizeAssistantForTripAsync(
        Guid tripId,
        Guid userId,
        Guid operatorId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new TripCrewAuthorizationOutcome(
            scenario.AssistantDenied
                ? TripCrewAuthorizationOutcomeKind.Denied
                : TripCrewAuthorizationOutcomeKind.Authorized));

    public Task<TripCargoOutcome> LoadCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        decimal volumeM3,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref scenario.LoadCargoCalls);
        return Task.FromResult(scenario.CargoFailure
            ? new TripCargoOutcome(TripCargoOutcomeKind.TransportError, "simulated cargo failure")
            : new TripCargoOutcome(TripCargoOutcomeKind.Success, null));
    }

    public Task<TripCargoOutcome> LoadCargoAsync(
        Guid tripId,
        Guid parcelId,
        decimal weightKg,
        CancellationToken cancellationToken = default)
        => LoadCargoAsync(tripId, parcelId, weightKg, 0.0001m, cancellationToken);

    public Task<TripSnapshotOutcome> GetTripParcelSnapshotAsync(Guid tripId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<RouteOwnershipOutcome> ValidateRouteOwnershipAsync(Guid routeId, Guid operatorId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<ParcelTripSearchOutcome> SearchAvailableParcelTripsAsync(Guid originStationId, Guid destinationStationId, DateOnly departureDate, decimal estimatedWeightKg, decimal estimatedVolumeM3, ParcelSizeCategory sizeCategory, int page, int pageSize, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<ParcelTripSearchOutcome> SearchAvailableParcelTripsAsync(Guid originStationId, Guid destinationStationId, DateOnly departureDate, decimal estimatedWeightKg, ParcelSizeCategory sizeCategory, int page, int pageSize, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<TripCargoOutcome> ReserveCargoAsync(Guid tripId, Guid parcelId, decimal weightKg, decimal volumeM3, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<TripCargoOutcome> ReserveCargoAsync(Guid tripId, Guid parcelId, decimal weightKg, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<TripCargoOutcome> ReserveCargoWithOverrideAsync(Guid tripId, Guid parcelId, decimal weightKg, decimal volumeM3, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<TripCargoOutcome> GetCargoCapacityAsync(Guid tripId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<TripCargoOutcome> RemeasureCargoAsync(Guid tripId, Guid parcelId, decimal weightKg, decimal volumeM3, bool allowCapacityOverflow = false, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<TripCargoOutcome> ReleaseCargoAsync(Guid tripId, Guid parcelId, decimal weightKg, decimal volumeM3, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public Task<TripCargoOutcome> ReleaseCargoAsync(Guid tripId, Guid parcelId, decimal weightKg, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}

internal sealed class ControlledUnitOfWork : IUnitOfWork
{
    private readonly ParcelDbContext db;
    private readonly Day29AssistantLoadScenario scenario;
    private IDbContextTransaction? transaction;

    public ControlledUnitOfWork(ParcelDbContext db, Day29AssistantLoadScenario scenario)
    {
        this.db = db;
        this.scenario = scenario;
    }

    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var localTransaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var result = await operation();
                await db.SaveChangesAsync(ct);
                if (scenario.ConsumeCommitFailure())
                {
                    throw new InvalidOperationException("simulated commit failure");
                }

                await localTransaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await localTransaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    public async Task BeginTransactionAsync(CancellationToken ct)
        => transaction = await db.Database.BeginTransactionAsync(ct);

    public async Task CommitAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        await transaction!.CommitAsync(ct);
        await transaction.DisposeAsync();
        transaction = null;
    }

    public async Task RollbackAsync(CancellationToken ct)
    {
        if (transaction is null)
        {
            return;
        }

        await transaction.RollbackAsync(ct);
        await transaction.DisposeAsync();
        transaction = null;
    }
}

internal class Day29RedisConnection : DispatchProxy
{
    private static readonly ConcurrentDictionary<string, RedisValue> Store = new();
    private static readonly object StoreLock = new();

    public static IConnectionMultiplexer Create()
        => DispatchProxy.Create<IConnectionMultiplexer, Day29RedisConnection>()!;

    public static void Reset()
    {
        lock (StoreLock)
        {
            Store.Clear();
        }
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
        {
            return null;
        }

        if (targetMethod.Name == nameof(IConnectionMultiplexer.GetDatabase))
        {
            return Day29RedisDatabase.Create();
        }

        return DefaultValue(targetMethod.ReturnType);
    }

    private class Day29RedisDatabase : DispatchProxy
    {
        public static StackExchange.Redis.IDatabase Create()
            => DispatchProxy.Create<StackExchange.Redis.IDatabase, Day29RedisDatabase>()!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                return null;
            }

            return targetMethod.Name switch
            {
                nameof(StackExchange.Redis.IDatabase.KeyExistsAsync) => Task.FromResult(Store.ContainsKey(Key(args![0]!))),
                nameof(StackExchange.Redis.IDatabase.StringGetAsync) => Task.FromResult(
                    Store.TryGetValue(Key(args![0]!), out var value) ? value : RedisValue.Null),
                nameof(StackExchange.Redis.IDatabase.StringSetAsync) => Task.FromResult(Set(
                    Key(args![0]!),
                    (RedisValue)args[1]!,
                    (When)args[3]!)),
                nameof(StackExchange.Redis.IDatabase.ScriptEvaluateAsync) => EvaluateScript(args!),
                _ => DefaultValue(targetMethod.ReturnType),
            };
        }

        private static Task<RedisResult> EvaluateScript(object?[] args)
        {
            var keys = (RedisKey[])args[1]!;
            var values = (RedisValue[])args[2]!;
            lock (StoreLock)
            {
                if (keys.Length == 2)
                {
                    Store[Key(keys[1])] = values[2];
                    Store.TryRemove(Key(keys[0]), out _);
                }
                else if (keys.Length == 1)
                {
                    Store.TryRemove(Key(keys[0]), out _);
                }
            }

            return Task.FromResult(RedisResult.Create((RedisValue)1));
        }

        private static bool Set(string key, RedisValue value, When when)
        {
            if (when == When.NotExists)
            {
                return Store.TryAdd(key, value);
            }

            Store[key] = value;
            return true;
        }
    }

    private static string Key(object key) => key.ToString() ?? string.Empty;

    private static object? DefaultValue(Type returnType)
    {
        if (returnType == typeof(void))
        {
            return null;
        }

        if (returnType == typeof(Task))
        {
            return Task.CompletedTask;
        }

        return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
    }
}
