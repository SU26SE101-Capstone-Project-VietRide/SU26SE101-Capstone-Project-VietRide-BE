using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using StackExchange.Redis;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Parcel.Infrastructure;
using VietRide.Parcel.Infrastructure.Http;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Web.Authentication;

namespace VietRide.Parcel.IntegrationTests;

public sealed class BatchParcelRouteFareEndpointTests
    : IClassFixture<BatchParcelRouteFareWebApplicationFactory>
{
    private readonly BatchParcelRouteFareWebApplicationFactory factory;

    public BatchParcelRouteFareEndpointTests(BatchParcelRouteFareWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task BatchParcelRouteFare_RequiresOperatorAdminTenantAndIdempotencyKey()
    {
        await factory.InitializeDatabaseAsync();
        var routeId = Guid.NewGuid();
        var body = ValidBody();

        using (var anonymous = factory.CreateClient())
        {
            anonymous.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
            var response = await anonymous.PutAsJsonAsync(BatchPath(routeId), body);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        foreach (var role in new[] { "PASSENGER", "OPERATOR_STAFF" })
        {
            using var wrongRole = factory.CreateAuthenticatedClient(role, Guid.NewGuid());
            wrongRole.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
            var response = await wrongRole.PutAsJsonAsync(BatchPath(routeId), body);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using (var missingTenant = factory.CreateAuthenticatedClient("OPERATOR_ADMIN", null))
        {
            missingTenant.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
            var response = await missingTenant.PutAsJsonAsync(BatchPath(routeId), body);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using (var missingKey = factory.CreateAuthenticatedClient("OPERATOR_ADMIN", Guid.NewGuid()))
        {
            var response = await missingKey.PutAsJsonAsync(BatchPath(routeId), body);
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await ReadErrorCodeAsync(response)).Should().Be("IDEMPOTENCY_KEY_REQUIRED");
        }
    }

    [Fact]
    public async Task BatchParcelRouteFare_InvalidItemsReturnValidationEnvelopeBeforePersistence()
    {
        await factory.InitializeDatabaseAsync();
        var operatorId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        var invalidBodies = new object[]
        {
            new { effectiveFrom = "2026-08-01T00:00:00+07:00", effectiveUntil = (string?)null, items = Array.Empty<object>() },
            new
            {
                effectiveFrom = "2026-08-01T00:00:00+07:00",
                effectiveUntil = (string?)null,
                items = Enumerable.Range(0, 5).Select(index => new
                {
                    sizeCategory = ((ParcelSizeCategory)(index % 4)).ToString(),
                    priceVnd = 50_000 + index,
                }).ToArray(),
            },
            new
            {
                effectiveFrom = "2026-08-01T00:00:00+07:00",
                effectiveUntil = (string?)null,
                items = new[]
                {
                    new { sizeCategory = "SMALL", priceVnd = 50_000 },
                    new { sizeCategory = "small", priceVnd = 60_000 },
                },
            },
            new
            {
                effectiveFrom = "2026-08-01T00:00:00+07:00",
                effectiveUntil = (string?)null,
                items = new[] { new { sizeCategory = "UNKNOWN", priceVnd = 50_000 } },
            },
            new
            {
                effectiveFrom = "2026-08-01T00:00:00+07:00",
                effectiveUntil = (string?)null,
                items = new[] { new { sizeCategory = "SMALL", priceVnd = 0 } },
            },
            new
            {
                effectiveFrom = "2026-08-01T00:00:00+07:00",
                effectiveUntil = "2026-08-01T00:00:00+07:00",
                items = new[] { new { sizeCategory = "SMALL", priceVnd = 50_000 } },
            },
        };

        foreach (var body in invalidBodies)
        {
            using var client = factory.CreateAuthenticatedClient("OPERATOR_ADMIN", operatorId);
            client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
            var response = await client.PutAsJsonAsync(BatchPath(routeId), body);
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await ReadErrorCodeAsync(response)).Should().Be("VALIDATION_ERROR");
        }

        using (var fractionalClient = factory.CreateAuthenticatedClient("OPERATOR_ADMIN", operatorId))
        {
            fractionalClient.DefaultRequestHeaders.Add(
                "Idempotency-Key",
                Guid.NewGuid().ToString("D"));
            using var fractionalBody = new StringContent(
                """
                {
                  "effectiveFrom": "2026-08-01T00:00:00+07:00",
                  "effectiveUntil": null,
                  "items": [{ "sizeCategory": "SMALL", "priceVnd": 50000.5 }]
                }
                """,
                Encoding.UTF8,
                "application/json");
            var response = await fractionalClient.PutAsync(BatchPath(routeId), fractionalBody);
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await ReadErrorCodeAsync(response)).Should().Be("VALIDATION_ERROR");
        }

        (await factory.ReadFaresAsync(routeId)).Should().BeEmpty();
    }

    [Fact]
    public async Task BatchParcelRouteFare_UpdatesAndCreatesAtomicallyInRequestOrder()
    {
        await factory.InitializeDatabaseAsync();
        var operatorId = Guid.NewGuid();
        var previousOperatorId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        await factory.SeedFareAsync(previousOperatorId, routeId, ParcelSizeCategory.MEDIUM, 70_000);
        using var client = factory.CreateAuthenticatedClient("OPERATOR_ADMIN", operatorId);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));

        var response = await client.PutAsJsonAsync(
            BatchPath(routeId),
            new
            {
                operatorId = Guid.NewGuid(),
                effectiveFrom = "2026-08-01T00:00:00+07:00",
                effectiveUntil = "2026-09-01T00:00:00+07:00",
                items = new[]
                {
                    new { sizeCategory = "MEDIUM", priceVnd = 80_000 },
                    new { sizeCategory = "SMALL", priceVnd = 50_000 },
                },
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = root.GetProperty("data");
        data.GetProperty("routeId").GetGuid().Should().Be(routeId);
        var items = data.GetProperty("items").EnumerateArray().ToArray();
        items.Select(item => item.GetProperty("sizeCategory").GetString())
            .Should().Equal("MEDIUM", "SMALL");
        items.Select(item => item.GetProperty("created").GetBoolean())
            .Should().Equal(false, true);
        items.Select(item => item.GetProperty("priceVnd").GetInt64())
            .Should().Equal(80_000, 50_000);
        items.Should().OnlyContain(item =>
            item.GetProperty("effectiveFrom").GetDateTimeOffset()
                == new DateTimeOffset(2026, 7, 31, 17, 0, 0, TimeSpan.Zero)
            && item.GetProperty("effectiveUntil").GetDateTimeOffset()
                == new DateTimeOffset(2026, 8, 31, 17, 0, 0, TimeSpan.Zero));

        var persisted = await factory.ReadFaresAsync(routeId);
        persisted.Should().HaveCount(2);
        persisted.Should().OnlyContain(fare => fare.OperatorId == operatorId);
        persisted.Single(fare => fare.SizeCategory == ParcelSizeCategory.MEDIUM)
            .PriceVnd.Amount.Should().Be(80_000);
        persisted.Single(fare => fare.SizeCategory == ParcelSizeCategory.SMALL)
            .PriceVnd.Amount.Should().Be(50_000);
    }

    [Fact]
    public async Task BatchParcelRouteFare_ConcurrentDistinctKeysSerializePhysicalUpsert()
    {
        await factory.InitializeDatabaseAsync();
        var operatorId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        using var firstClient = factory.CreateAuthenticatedClient("OPERATOR_ADMIN", operatorId);
        using var secondClient = factory.CreateAuthenticatedClient("OPERATOR_ADMIN", operatorId);
        firstClient.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        secondClient.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));

        var firstRequest = firstClient.PutAsJsonAsync(
            BatchPath(routeId),
            new
            {
                effectiveFrom = "2026-08-01T00:00:00+07:00",
                effectiveUntil = (string?)null,
                items = new[] { new { sizeCategory = "SMALL", priceVnd = 50_000 } },
            });
        var secondRequest = secondClient.PutAsJsonAsync(
            BatchPath(routeId),
            new
            {
                effectiveFrom = "2026-08-02T00:00:00+07:00",
                effectiveUntil = (string?)null,
                items = new[] { new { sizeCategory = "SMALL", priceVnd = 60_000 } },
            });

        var responses = await Task.WhenAll(firstRequest, secondRequest);

        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
        var createdFlags = new List<bool>();
        foreach (var response in responses)
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            createdFlags.Add(json.RootElement
                .GetProperty("data")
                .GetProperty("items")[0]
                .GetProperty("created")
                .GetBoolean());
            response.Dispose();
        }

        createdFlags.Should().BeEquivalentTo([true, false]);
        var persisted = await factory.ReadFaresAsync(routeId);
        persisted.Should().ContainSingle();
        persisted.Single().OperatorId.Should().Be(operatorId);
        persisted.Single().PriceVnd.Amount.Should().BeOneOf(50_000, 60_000);
    }

    [Fact]
    public async Task BatchParcelRouteFare_ConcurrentSingleCreateUsesSamePhysicalKeyLock()
    {
        await factory.InitializeDatabaseAsync();
        var operatorId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        await factory.InstallBatchInsertDelayTriggerAsync();

        try
        {
            using var batchClient = factory.CreateAuthenticatedClient("OPERATOR_ADMIN", operatorId);
            using var createClient = factory.CreateAuthenticatedClient("OPERATOR_ADMIN", operatorId);
            batchClient.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
            createClient.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));

            var batchTask = batchClient.PutAsJsonAsync(BatchPath(routeId), ValidBody());
            await Task.Delay(TimeSpan.FromMilliseconds(150));
            var createTask = createClient.PostAsJsonAsync(
                "/v1/operator/parcel-route-fares",
                new
                {
                    routeId,
                    sizeCategory = "SMALL",
                    priceVnd = 70_000,
                    effectiveFrom = "2026-08-01T00:00:00+07:00",
                    effectiveUntil = (string?)null,
                });

            await Task.WhenAll(batchTask, createTask);

            batchTask.Result.StatusCode.Should().Be(HttpStatusCode.OK);
            createTask.Result.StatusCode.Should().BeOneOf(
                HttpStatusCode.Created,
                HttpStatusCode.Conflict);
            if (createTask.Result.StatusCode == HttpStatusCode.Conflict)
            {
                (await ReadErrorCodeAsync(createTask.Result)).Should().Be("FARE_ALREADY_EXISTS");
            }

            var persisted = await factory.ReadFaresAsync(routeId);
            persisted.Should().ContainSingle();
            persisted.Single().SizeCategory.Should().Be(ParcelSizeCategory.SMALL);
            persisted.Single().OperatorId.Should().Be(operatorId);
        }
        finally
        {
            await factory.RemoveBatchInsertDelayTriggerAsync();
        }
    }

    [Fact]
    public async Task BatchParcelRouteFare_PersistenceFailureRollsBackWholeBatch()
    {
        await factory.InitializeDatabaseAsync();
        var operatorId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        await factory.SeedFareAsync(operatorId, routeId, ParcelSizeCategory.MEDIUM, 70_000);
        await factory.InstallSmallFareFailureTriggerAsync();

        try
        {
            using var client = factory.CreateAuthenticatedClient("OPERATOR_ADMIN", operatorId);
            client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
            var response = await client.PutAsJsonAsync(
                BatchPath(routeId),
                new
                {
                    effectiveFrom = "2026-08-01T00:00:00+07:00",
                    effectiveUntil = (string?)null,
                    items = new[]
                    {
                        new { sizeCategory = "MEDIUM", priceVnd = 80_000 },
                        new { sizeCategory = "SMALL", priceVnd = 50_000 },
                    },
                });

            response.IsSuccessStatusCode.Should().BeFalse();
            var persisted = await factory.ReadFaresAsync(routeId);
            persisted.Should().ContainSingle();
            persisted.Single().SizeCategory.Should().Be(ParcelSizeCategory.MEDIUM);
            persisted.Single().PriceVnd.Amount.Should().Be(70_000);
        }
        finally
        {
            await factory.RemoveSmallFareFailureTriggerAsync();
        }
    }

    [Fact]
    public async Task BatchParcelRouteFare_IdempotencyReplayDoesNotWriteTwice()
    {
        await factory.InitializeDatabaseAsync();
        var operatorId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        var key = Guid.NewGuid().ToString("D");
        using var client = factory.CreateAuthenticatedClient("OPERATOR_ADMIN", operatorId);
        client.DefaultRequestHeaders.Add("Idempotency-Key", key);
        var body = ValidBody();

        var first = await client.PutAsJsonAsync(BatchPath(routeId), body);
        var firstPayload = await first.Content.ReadAsStringAsync();
        var replay = await client.PutAsJsonAsync(BatchPath(routeId), body);
        var replayPayload = await replay.Content.ReadAsStringAsync();

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        replayPayload.Should().Be(firstPayload);
        (await factory.ReadFaresAsync(routeId)).Should().ContainSingle();
    }

    [Fact]
    public async Task BatchParcelRouteFare_PreservesSingleSizeCreatePatchAndListEndpoints()
    {
        await factory.InitializeDatabaseAsync();
        var operatorId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        using var client = factory.CreateAuthenticatedClient("OPERATOR_ADMIN", operatorId);

        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        var create = await client.PostAsJsonAsync(
            "/v1/operator/parcel-route-fares",
            new
            {
                routeId,
                sizeCategory = "LARGE",
                priceVnd = 90_000,
                effectiveFrom = "2026-08-01T00:00:00Z",
                effectiveUntil = (string?)null,
            });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        var patch = await client.PatchAsJsonAsync(
            $"/v1/operator/parcel-route-fares/{routeId:D}/LARGE",
            new { priceVnd = 95_000 });
        patch.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await client.GetAsync(
            $"/v1/operator/parcel-route-fares?routeId={routeId:D}&sizeCategory=LARGE");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();
        items.Should().ContainSingle();
        items.Single().GetProperty("priceVnd").GetInt64().Should().Be(95_000);
    }

    [Fact]
    public async Task BatchParcelRouteFare_SwaggerDocumentsOperationResponsesAndIdempotencyHeader()
    {
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operation = json.RootElement
            .GetProperty("paths")
            .GetProperty("/v1/operator/parcel-route-fares/{routeId}/batch")
            .GetProperty("put");
        operation.GetProperty("parameters").EnumerateArray().Should().Contain(parameter =>
            parameter.GetProperty("name").GetString() == "Idempotency-Key"
            && parameter.GetProperty("in").GetString() == "header"
            && parameter.GetProperty("required").GetBoolean());
        var responses = operation.GetProperty("responses");
        responses.TryGetProperty("200", out _).Should().BeTrue();
        responses.TryGetProperty("403", out _).Should().BeTrue();
        responses.TryGetProperty("404", out _).Should().BeTrue();
        responses.TryGetProperty("409", out _).Should().BeTrue();
        responses.TryGetProperty("422", out _).Should().BeTrue();
        responses.TryGetProperty("503", out _).Should().BeTrue();
    }

    private static string BatchPath(Guid routeId)
        => $"/v1/operator/parcel-route-fares/{routeId:D}/batch";

    private static object ValidBody()
        => new
        {
            effectiveFrom = "2026-08-01T00:00:00+07:00",
            effectiveUntil = (string?)null,
            items = new[] { new { sizeCategory = "SMALL", priceVnd = 50_000 } },
        };

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("error").GetProperty("code").GetString();
    }
}

public sealed class BatchParcelRouteFareWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string TestSecret = "test-secret-at-least-32-chars-long-xxxxx";
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private readonly string connectionString = BuildTestConnectionString();
    private bool databaseCreated;
    private bool initialized;

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
            services.AddSingleton<IConnectionMultiplexer>(
                BatchParcelRouteFareRedisConnectionMultiplexer.Create());
            services.RemoveAll<ITripServiceClient>();
            services.AddScoped<ITripServiceClient, DevTripServiceClient>();
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

            if (!databaseCreated)
            {
                await using var connection = new NpgsqlConnection(MaintenanceConnectionString());
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE \"{DatabaseName()}\";";
                await command.ExecuteNonQueryAsync();
                databaseCreated = true;
            }

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

    public HttpClient CreateAuthenticatedClient(string role, Guid? operatorId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            InternalJwtAuthenticationExtensions.HeaderName,
            $"Bearer {MintJwt(role, operatorId)}");
        return client;
    }

    public async Task SeedFareAsync(
        Guid operatorId,
        Guid routeId,
        ParcelSizeCategory sizeCategory,
        long priceVnd)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ParcelDbContext>();
        db.ParcelRouteFares.Add(ParcelRouteFare.Create(
            routeId,
            sizeCategory,
            operatorId,
            Money.FromRaw(priceVnd),
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<ParcelRouteFare>> ReadFaresAsync(Guid routeId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ParcelDbContext>();
        return await db.ParcelRouteFares
            .AsNoTracking()
            .Where(fare => fare.RouteId == routeId)
            .OrderBy(fare => fare.SizeCategory)
            .ToListAsync();
    }

    public async Task InstallSmallFareFailureTriggerAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ParcelDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE OR REPLACE FUNCTION vietride_parcel.reject_small_batch_fare()
            RETURNS trigger AS $$
            BEGIN
                IF NEW.size_category = 'SMALL'::vietride_parcel.parcel_size_category THEN
                    RAISE EXCEPTION 'simulated batch persistence failure';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER trg_reject_small_batch_fare
            BEFORE INSERT OR UPDATE ON vietride_parcel.parcel_route_fares
            FOR EACH ROW EXECUTE FUNCTION vietride_parcel.reject_small_batch_fare();
            """);
    }

    public async Task RemoveSmallFareFailureTriggerAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ParcelDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER IF EXISTS trg_reject_small_batch_fare
                ON vietride_parcel.parcel_route_fares;
            DROP FUNCTION IF EXISTS vietride_parcel.reject_small_batch_fare();
            """);
    }

    public async Task InstallBatchInsertDelayTriggerAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ParcelDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE OR REPLACE FUNCTION vietride_parcel.delay_small_batch_insert()
            RETURNS trigger AS $$
            BEGIN
                IF NEW.size_category = 'SMALL'::vietride_parcel.parcel_size_category
                    AND NEW.price_vnd = 50000 THEN
                    PERFORM pg_sleep(1);
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER trg_delay_small_batch_insert
            BEFORE INSERT ON vietride_parcel.parcel_route_fares
            FOR EACH ROW EXECUTE FUNCTION vietride_parcel.delay_small_batch_insert();
            """);
    }

    public async Task RemoveBatchInsertDelayTriggerAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ParcelDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER IF EXISTS trg_delay_small_batch_insert
                ON vietride_parcel.parcel_route_fares;
            DROP FUNCTION IF EXISTS vietride_parcel.delay_small_batch_insert();
            """);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (databaseCreated)
        {
            await using var connection = new NpgsqlConnection(MaintenanceConnectionString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS \"{DatabaseName()}\" WITH (FORCE);";
            await command.ExecuteNonQueryAsync();
        }

        initializationLock.Dispose();
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
            Database = $"vietride_parcel_ui09_batch_fares_{Guid.NewGuid():N}",
        }.ConnectionString;
    }

    private static string MintJwt(string role, Guid? operatorId)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString("D")),
            new("role", role),
        };
        if (operatorId.HasValue)
        {
            claims.Add(new Claim("operatorId", operatorId.Value.ToString("D")));
        }

        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims: claims,
            notBefore: now.AddSeconds(-5),
            expires: now.AddMinutes(15),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

internal class BatchParcelRouteFareRedisConnectionMultiplexer : DispatchProxy
{
    private static readonly object Sync = new();
    private static Dictionary<string, RedisValue> store = new();

    public static IConnectionMultiplexer Create()
    {
        lock (Sync)
        {
            store = new Dictionary<string, RedisValue>();
        }

        return DispatchProxy.Create<IConnectionMultiplexer, BatchParcelRouteFareRedisConnectionMultiplexer>()!;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
        {
            return null;
        }

        return targetMethod.Name == nameof(IConnectionMultiplexer.GetDatabase)
            ? InMemoryDatabase.Create()
            : targetMethod.ReturnType == typeof(void)
                ? null
                : targetMethod.ReturnType.IsValueType
                    ? Activator.CreateInstance(targetMethod.ReturnType)
                    : null;
    }

    private class InMemoryDatabase : DispatchProxy
    {
        public static IDatabase Create()
            => DispatchProxy.Create<IDatabase, InMemoryDatabase>()!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                return null;
            }

            lock (Sync)
            {
                return targetMethod.Name switch
                {
                    nameof(IDatabase.KeyExistsAsync) =>
                        Task.FromResult(store.ContainsKey(Key(args![0]!))),
                    nameof(IDatabase.StringGetAsync) =>
                        Task.FromResult(store.TryGetValue(Key(args![0]!), out var value)
                            ? value
                            : RedisValue.Null),
                    nameof(IDatabase.StringSetAsync) =>
                        Task.FromResult(Set(
                            Key(args![0]!),
                            (RedisValue)args[1]!,
                            (When)args[3]!)),
                    nameof(IDatabase.ScriptEvaluateAsync) =>
                        Task.FromResult(EvaluateScript(
                            (RedisKey[])args![1]!,
                            (RedisValue[])args[2]!)),
                    _ => targetMethod.ReturnType == typeof(void)
                        ? null
                        : targetMethod.ReturnType.IsValueType
                            ? Activator.CreateInstance(targetMethod.ReturnType)
                            : null,
                };
            }
        }

        private static RedisResult EvaluateScript(RedisKey[] keys, RedisValue[] values)
        {
            if (keys.Length == 2 && values.Length == 4 && store.ContainsKey(Key(keys[0])))
            {
                store[Key(keys[1])] = values[2];
                store.Remove(Key(keys[0]));
                return RedisResult.Create((RedisValue)1);
            }

            if (keys.Length == 1 && values.Length == 1 && store.Remove(Key(keys[0])))
            {
                return RedisResult.Create((RedisValue)1);
            }

            return RedisResult.Create((RedisValue)0);
        }

        private static bool Set(string key, RedisValue value, When when)
        {
            if (when == When.NotExists && store.ContainsKey(key))
            {
                return false;
            }

            store[key] = value;
            return true;
        }

        private static string Key(object key)
            => key.ToString() ?? string.Empty;
    }
}
