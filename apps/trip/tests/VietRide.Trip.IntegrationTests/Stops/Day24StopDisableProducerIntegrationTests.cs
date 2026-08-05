using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using VietRide.Trip.Api.Controllers;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Features.Stops;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests.Stops;

public sealed class Day24StopDisableProducerIntegrationTests
{
    [Fact]
    public void StopDisable_PreservesSoftDeleteColumnAndDeactivatesStop()
    {
        var stop = Stop.Create(Guid.NewGuid(), "Integration stop", 10, 10);
        stop.Disable(null);
        stop.IsActive.Should().BeFalse();
        stop.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void OperatorDelete_IsRoleGatedAndBodylessIdempotentMutation()
    {
        var method = typeof(OperatorStopsController).GetMethod(nameof(OperatorStopsController.DeleteAsync))!;
        method.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("OPERATOR_ADMIN");
        method.GetCustomAttributes().Should().Contain(attribute => attribute.GetType().Name == "RequireIdempotencyAttribute");
        method.GetCustomAttributes().Single(attribute => attribute.GetType().Name == "RequireIdempotencyAttribute")
            .GetType().GetProperty("AllowRequestBody")!.GetValue(method.GetCustomAttributes().Single(attribute => attribute.GetType().Name == "RequireIdempotencyAttribute"))
            .Should().Be(false);
        method.GetParameters().Should().NotContain(parameter => parameter.Name == "request");
        typeof(VietRide.Trip.Application.Features.Stops.DisableStopResponse)
            .GetProperties().Select(property => property.Name)
            .Should().Contain(["Stop", "Warning"])
            .And.NotContain("ActiveBookingCount");
    }

    [Fact]
    public void DeleteContract_UsesAdr004SuccessEnvelopeAndConflictStatuses()
    {
        var method = typeof(OperatorStopsController).GetMethod(nameof(OperatorStopsController.DeleteAsync))!;
        var statuses = method.GetCustomAttributes<Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode).ToArray();
        statuses.Should().Contain([200, 403, 404, 409, 422]);
    }

    [Fact]
    public async Task Delete_UsesRealMiddlewareForBodylessReplayAndFingerprintMismatch()
    {
        var stop = Stop.Create(Guid.NewGuid(), "A", 1, 2);
        var response = new DisableStopResponse(new StopDto(stop.Id, stop.OperatorId, stop.Name, null, 1, 2, null, null, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), null);
        var mediator = new RecordingMediator(response);
        using var factory = new StopWebApplicationFactory(mediator);
        using var client = factory.CreateClient();
        var key = Guid.NewGuid().ToString("D");
        async Task<HttpResponseMessage> Send(Guid? replacement = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, $"/v1/operator/stops/{stop.Id}?replacedByStopId={replacement}");
            request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateJwt(stop.OperatorId)}");
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
            return await client.SendAsync(request);
        }

        using var first = await Send();
        using var replay = await Send();
        using var mismatch = await Send(Guid.NewGuid());
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadAsStringAsync();
        firstBody.Should().Contain("\"warning\":null");
        firstBody.Should().NotContain("ActiveBookingCount");
        (await replay.Content.ReadAsStringAsync()).Should().Be(firstBody);
        mismatch.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        mediator.SendCount.Should().Be(1);

        using var missingKey = new HttpRequestMessage(HttpMethod.Delete, $"/v1/operator/stops/{stop.Id}");
        missingKey.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateJwt(stop.OperatorId)}");
        using var missingResponse = await client.SendAsync(missingKey);
        missingResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        using var bodyRequest = new HttpRequestMessage(HttpMethod.Delete, $"/v1/operator/stops/{stop.Id}")
        { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        bodyRequest.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateJwt(stop.OperatorId)}");
        bodyRequest.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var bodyResponse = await client.SendAsync(bodyRequest);
        bodyResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Delete_RealHandlerPath_MigratesAndPersistsOutbox()
    {
        using var factory = new RealStopWebApplicationFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripDbContext>();
        await db.Database.MigrateAsync();
        var beforeEvents = await db.OutboxEvents.CountAsync(x => x.EventType == "trip.stop.disabled");
        var stop = Stop.Create(Guid.NewGuid(), "real-handler", 1, 2);
        db.Stops.Add(stop);
        await db.SaveChangesAsync();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/v1/operator/stops/{stop.Id}");
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateJwt(stop.OperatorId)}");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TripDbContext>();
        var persistedStop = await verifyDb.Stops.AsNoTracking().SingleAsync(x => x.Id == stop.Id);
        persistedStop.IsActive.Should().BeFalse();
        persistedStop.DeletedAt.Should().BeNull();
        var events = await verifyDb.OutboxEvents.AsNoTracking()
            .Where(x => x.EventType == "trip.stop.disabled")
            .OrderByDescending(x => x.CreatedAt).Take(10).ToListAsync();
        events = events.Where(x => JsonDocument.Parse(x.Payload).RootElement.GetProperty("stopId").GetGuid() == stop.Id).ToList();
        events.Should().ContainSingle();
        events[0].Status.Should().Be(VietRide.Shared.Persistence.Outbox.OutboxEventStatus.PENDING);
        events[0].PublishedAt.Should().BeNull();
        events[0].Id.Should().NotBe(stop.Id);
        events[0].Id.Should().NotBeEmpty();
        using var eventPayload = JsonDocument.Parse(events[0].Payload);
        eventPayload.RootElement.GetProperty("eventId").GetGuid().Should().Be(events[0].Id);
        (await verifyDb.OutboxEvents.CountAsync(x => x.EventType == "trip.stop.disabled")).Should().Be(beforeEvents + 1);
    }


    private static string CreateJwt(Guid operatorId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-secret-at-least-32-characters-long"));
        var token = new JwtSecurityToken("vietride-gateway", "vietride-internal", [new Claim("sub", operatorId.ToString()), new Claim(ClaimTypes.Role, "OPERATOR_ADMIN"), new Claim("operatorId", operatorId.ToString())], expires: DateTime.UtcNow.AddMinutes(2), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class StopWebApplicationFactory(IMediator mediator) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Environment.SetEnvironmentVariable("INTERNAL_JWT_SECRET", "test-secret-at-least-32-characters-long");
            builder.UseSetting("INTERNAL_JWT_SECRET", "test-secret-at-least-32-characters-long");
            builder.UseSetting("Trip:BackgroundWorkers:Enabled", "false");
            builder.UseSetting("REDIS_URL", "127.0.0.1:6379");
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services => { services.RemoveAll<IMediator>(); services.AddSingleton(mediator); });
        }
    }

    private sealed class RealStopWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("INTERNAL_JWT_SECRET", "test-secret-at-least-32-characters-long");
            builder.UseSetting("Trip:BackgroundWorkers:Enabled", "false");
            builder.UseSetting("ConnectionStrings:Default", VietRideWebApplicationFactory.ResolveConnectionString("postgres"));
            builder.UseSetting("REDIS_URL", "127.0.0.1:6379");
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<TripDbContext>>();
                services.AddDbContext<TripDbContext>((provider, options) =>
                    options.UseNpgsql(
                            provider.GetRequiredService<Npgsql.NpgsqlDataSource>(),
                            npgsql => npgsql.MigrationsHistoryTable(
                                "__ef_migrations_history",
                                TripDbContext.SchemaName))
                        .ConfigureWarnings(warnings => warnings.Ignore(
                            Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)));
                services.RemoveAll<IIdentityInternalClient>();
                services.AddScoped<IIdentityInternalClient, AllowedIdentityClient>();
            });
        }
    }

    private sealed class AllowedIdentityClient : IIdentityInternalClient
    {
        public Task<OperatorWriteEligibilityValidation> ValidateOperatorCanWriteAsync(Guid operatorId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperatorWriteEligibilityValidation.Allowed());
        public Task<IdentityUserLookupResult> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(IdentityUserLookupResult.ValidationFailure("unused"));
    }


    private sealed class RecordingMediator(DisableStopResponse response) : IMediator
    {
        public int SendCount { get; private set; }
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) { SendCount++; return Task.FromResult((TResponse)(object)response); }
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) { SendCount++; return Task.FromResult<object?>(response); }
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => Empty<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => Empty<object?>();
        private static async IAsyncEnumerable<T> Empty<T>() { await Task.CompletedTask; yield break; }
    }
}
