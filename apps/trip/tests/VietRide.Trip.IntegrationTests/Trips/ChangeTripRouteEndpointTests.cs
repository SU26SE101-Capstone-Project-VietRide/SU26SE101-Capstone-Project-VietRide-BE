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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Web.Idempotency;
using VietRide.Trip.Api.Controllers;
using VietRide.Trip.Api.Filters;
using VietRide.Trip.Application.Events;
using VietRide.Trip.Application.Features.Trips;
using Xunit;

namespace VietRide.Trip.IntegrationTests.Trips;

public sealed class ChangeTripRouteEndpointTests
{
    private static string Jwt(string role, Guid? operatorId = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-secret-at-least-32-characters-long"));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "vietride-gateway", audience: "vietride-internal",
            claims: [new Claim(ClaimTypes.Role, role), new Claim("operatorId", (operatorId ?? Guid.NewGuid()).ToString()), new Claim("sub", Guid.NewGuid().ToString())],
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));
    }

    [Fact]
    public async Task SuccessReturnsRatifiedShape()
    {
        var tripId = Guid.NewGuid();
        var alternativeRouteId = Guid.NewGuid();
        var affectedBookingId = Guid.NewGuid();
        var candidateStop = new TripRouteChangedCandidateStop(
            Guid.NewGuid(),
            null,
            "Candidate stop",
            1,
            DateTimeOffset.Parse("2026-07-23T01:45:00Z"));
        var mediator = new StubMediator(_ => new ChangeTripRouteResponse(
            tripId,
            "SCHEDULED",
            alternativeRouteId,
            [new TripRouteChangedAffectedBooking(affectedBookingId, [candidateStop])]));
        using var factory = new RouteFactory(mediator);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/operator/trips/{tripId}/change-route");
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", "Bearer " + Jwt("OPERATOR_ADMIN"));
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        request.Content = new StringContent(
            $"{{\"alternativeRouteId\":\"{alternativeRouteId:D}\"}}",
            Encoding.UTF8,
            "application/json");
        var response = await factory.CreateClient().SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["success", "statusCode", "data", "meta"]);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("statusCode").GetInt32().Should().Be(200);
        document.RootElement.GetProperty("meta").EnumerateObject()
            .Select(property => property.Name).Should().BeEquivalentTo("traceId", "timestamp");
        var data = document.RootElement.GetProperty("data");
        data.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            ["tripId", "status", "alternativeRouteId", "affectedBookings"]);
        data.GetProperty("tripId").GetGuid().Should().Be(tripId);
        data.GetProperty("status").GetString().Should().Be("SCHEDULED");
        data.GetProperty("alternativeRouteId").GetGuid().Should().Be(alternativeRouteId);
        var affectedBooking = data.GetProperty("affectedBookings")[0];
        affectedBooking.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("bookingId", "candidateStops");
        affectedBooking.GetProperty("bookingId").GetGuid().Should().Be(affectedBookingId);
        var serializedCandidate = affectedBooking.GetProperty("candidateStops")[0];
        serializedCandidate.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(
                "stopId",
                "stationId",
                "stationName",
                "sequence",
                "estimatedArrivalAt");
        serializedCandidate.GetProperty("stopId").GetGuid().Should().Be(candidateStop.StopId!.Value);
        serializedCandidate.GetProperty("stationId").ValueKind.Should().Be(JsonValueKind.Null);
        mediator.SendCount.Should().Be(1);
    }

    [Fact]
    public async Task AlternativeRouteWrongParentAndWrongTenantReturnRouteNotFound()
    {
        var owningOperatorId = Guid.NewGuid();
        var wrongParentAlternativeId = Guid.NewGuid();
        var ownedAlternativeId = Guid.NewGuid();
        var terminalAlternativeId = Guid.NewGuid();
        var mediator = new StubMediator(request =>
        {
            var command = request.Should().BeOfType<ChangeTripRouteCommand>().Subject;
            if (command.AlternativeRouteId == terminalAlternativeId)
                throw new CodedConflictException("TRIP_NOT_EDITABLE", "Trip cannot be edited.");
            if (command.OperatorId != owningOperatorId
                || command.AlternativeRouteId == wrongParentAlternativeId)
            {
                throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Alternative route was not found.");
            }

            return new ChangeTripRouteResponse(command.TripId, "SCHEDULED", ownedAlternativeId, []);
        });
        using var factory = new RouteFactory(mediator);
        using var client = factory.CreateClient();

        using var wrongParent = await client.SendAsync(CreateRequest(
            Guid.NewGuid(),
            owningOperatorId,
            Guid.NewGuid().ToString("D"),
            wrongParentAlternativeId));
        wrongParent.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertErrorCodeAsync(wrongParent, "ROUTE_NOT_FOUND");

        using var foreignTenant = await client.SendAsync(CreateRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D"),
            ownedAlternativeId));
        foreignTenant.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertErrorCodeAsync(foreignTenant, "ROUTE_NOT_FOUND");

        using var terminal = await client.SendAsync(CreateRequest(
            Guid.NewGuid(),
            owningOperatorId,
            Guid.NewGuid().ToString("D"),
            terminalAlternativeId));
        terminal.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertErrorCodeAsync(terminal, "TRIP_NOT_EDITABLE");
    }

    [Fact]
    public async Task BookingImpactSuccessAuthTenantAndTimeoutAreHandled()
    {
        var operatorId = Guid.NewGuid();
        var alternativeRouteId = Guid.NewGuid();
        var affectedBookingId = Guid.NewGuid();
        var candidateStop = new TripRouteChangedCandidateStop(
            null,
            Guid.NewGuid(),
            "Destination station",
            1,
            DateTimeOffset.Parse("2026-07-23T05:00:00Z"));
        var mediator = new StubMediator(request =>
        {
            var command = request.Should().BeOfType<ChangeTripRouteCommand>().Subject;
            command.OperatorId.Should().Be(operatorId);
            command.AlternativeRouteId.Should().Be(alternativeRouteId);
            return new ChangeTripRouteResponse(
                command.TripId,
                "SCHEDULED",
                alternativeRouteId,
                [new TripRouteChangedAffectedBooking(affectedBookingId, [candidateStop])]);
        });
        using var factory = new RouteFactory(mediator);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(CreateRequest(
            Guid.NewGuid(),
            operatorId,
            Guid.NewGuid().ToString("D"),
            alternativeRouteId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("data").GetProperty("affectedBookings")[0]
            .GetProperty("bookingId").GetGuid().Should().Be(affectedBookingId);
        mediator.SendCount.Should().Be(1);
    }
    [Fact] public void ThinControllerDispatchesMediatR() => typeof(TripsController).GetField("mediator", BindingFlags.Instance | BindingFlags.NonPublic).Should().NotBeNull();
    [Fact]
    public async Task UnauthenticatedReturns401AdrEnvelope()
    {
        using var factory = new RouteFactory(new StubMediator(_ => null));
        (await factory.CreateClient().PostAsync($"/v1/operator/trips/{Guid.NewGuid():D}/change-route", new StringContent("{}"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
    [Fact]
    public async Task NonAdminReturns403AdrEnvelope()
    {
        using var factory = new RouteFactory(new StubMediator(_ => null));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/operator/trips/{Guid.NewGuid():D}/change-route");
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", "Bearer " + Jwt("OPERATOR_STAFF"));
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        request.Content = new StringContent("{}");
        (await factory.CreateClient().SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
    [Fact]
    public async Task MissingOrMalformedIdempotencyKeyRejected()
    {
        var mediator = new StubMediator(_ => null);
        using var factory = new RouteFactory(mediator);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/operator/trips/{Guid.NewGuid():D}/change-route");
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", "Bearer " + Jwt("OPERATOR_ADMIN"));
        request.Content = new StringContent("{}");
        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var validAlternativeRouteId = Guid.NewGuid();
        var invalidBodies = new[]
        {
            "{}",
            "{\"alternativeRouteId\":null}",
            "{\"alternativeRouteId\":\"not-a-guid\"}",
            "{\"alternativeRouteId\":\"00000000-0000-0000-0000-000000000000\"}",
            $"{{\"alternativeRouteId\":\"{validAlternativeRouteId:D}\",\"unknown\":true}}",
        };
        foreach (var body in invalidBodies)
        {
            using var invalid = CreateRawRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid().ToString("D"),
                body);
            using var response = await client.SendAsync(invalid);
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            await AssertErrorCodeAsync(response, "VALIDATION_ERROR");
        }

        mediator.SendCount.Should().Be(0);
    }
    [Fact]
    public async Task ReplayAndMismatchFollowContract()
    {
        var mediator = new StubMediator(request =>
        {
            var command = request.Should().BeOfType<ChangeTripRouteCommand>().Subject;
            return new ChangeTripRouteResponse(
                command.TripId,
                "SCHEDULED",
                command.AlternativeRouteId,
                []);
        });
        using var factory = new RouteFactory(mediator);
        using var client = factory.CreateClient();
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var key = Guid.NewGuid().ToString("D");
        var alternativeRouteId = Guid.NewGuid();
        var token = Jwt("OPERATOR_ADMIN", operatorId);

        using var first = await client.SendAsync(CreateRequest(
            tripId, operatorId, key, alternativeRouteId, token));
        var firstBody = await first.Content.ReadAsStringAsync();
        using var replay = await client.SendAsync(CreateRequest(
            tripId, operatorId, key, alternativeRouteId, token));
        var replayBody = await replay.Content.ReadAsStringAsync();
        using var mismatch = await client.SendAsync(CreateRequest(
            tripId, operatorId, key, Guid.NewGuid(), token));

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        replayBody.Should().Be(firstBody);
        mediator.SendCount.Should().Be(1);
        mismatch.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCodeAsync(mismatch, "IDEMPOTENCY_KEY_MISMATCH");
    }

    [Fact]
    public async Task IdempotencyKeyMustBeUuidV4()
    {
        var mediator = new StubMediator(_ => null);
        using var factory = new RouteFactory(mediator);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(CreateRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "11111111-1111-1111-8111-111111111111",
            Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCodeAsync(response, "VALIDATION_ERROR");
        mediator.SendCount.Should().Be(0);
    }

    private static HttpRequestMessage CreateRequest(
        Guid tripId,
        Guid operatorId,
        string idempotencyKey,
        Guid alternativeRouteId,
        string? token = null)
        => CreateRawRequest(
            tripId,
            operatorId,
            idempotencyKey,
            $"{{\"alternativeRouteId\":\"{alternativeRouteId:D}\"}}",
            token);

    private static HttpRequestMessage CreateRawRequest(
        Guid tripId,
        Guid operatorId,
        string idempotencyKey,
        string body,
        string? token = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/operator/trips/{tripId:D}/change-route");
        request.Headers.TryAddWithoutValidation(
            "X-Internal-Auth",
            "Bearer " + (token ?? Jwt("OPERATOR_ADMIN", operatorId)));
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        request.Content = new StringContent(
            body,
            Encoding.UTF8,
            "application/json");
        return request;
    }

    private static async Task AssertErrorCodeAsync(HttpResponseMessage response, string expected)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be(expected);
    }

    private static MethodInfo Method() => typeof(TripsController).GetMethod(nameof(TripsController.ChangeRouteAsync))!;

    private sealed class RouteFactory(IMediator mediator) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Environment.SetEnvironmentVariable(
                "INTERNAL_JWT_SECRET",
                "test-secret-at-least-32-characters-long");
            builder.UseEnvironment("Testing")
                .UseSetting("INTERNAL_JWT_SECRET", "test-secret-at-least-32-characters-long")
                .UseSetting("Trip:BackgroundWorkers:Enabled", "false")
                .UseSetting(
                    "ConnectionStrings:Default",
                    "Host=localhost;Port=5432;Database=test;Username=vietride;Password=vietride_dev")
                .UseSetting("REDIS_URL", "localhost:6379");
            builder.ConfigureTestServices(services => { services.RemoveAll<IMediator>(); services.AddSingleton(mediator); });
        }
    }

    private sealed class StubMediator(Func<object, object?> responder) : IMediator
    {
        public int SendCount { get; private set; }
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) { SendCount++; return Task.FromResult((TResponse)responder(request)!); }
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) { SendCount++; return Task.FromResult(responder(request)); }
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => Empty<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => Empty<object?>();
        private static async IAsyncEnumerable<T> Empty<T>() { await Task.CompletedTask; yield break; }
    }
}
