using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Web.DependencyInjection;
using VietRide.Shared.Web.Middleware;
using VietRide.Trip.Application.Abstractions.SeatLock;
using VietRide.Trip.Application.Features.Internal.Trips.BookSeats;
using VietRide.Trip.Application.Features.Internal.Trips.GetTripSnapshot;
using VietRide.Trip.Application.Features.Internal.Trips.LockSeats;
using VietRide.Trip.Application.Features.Internal.Trips.ReleaseSeats;
using VietRide.Trip.Application.Features.Internal.Trips.Tracking;
using VietRide.Trip.Infrastructure.SeatLock;

namespace VietRide.Trip.IntegrationTests.Internal.Trips;

public sealed class InternalTripsEndpointTests
{
    [Fact]
    public async Task GetTrip_Happy_ReturnsRawDto()
    {
        var tripId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var assistantUserId = Guid.NewGuid();
        var snapshot = CreateSnapshot(tripId, driverUserId, assistantUserId);
        var mediator = new StubMediator(_ => snapshot);
        using var factory = new InternalTripsEndpointWebApplicationFactory(mediator);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(CreateAuthorizedRequest(HttpMethod.Get, $"/internal/v1/trips/{tripId}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.TryGetProperty("success", out _).Should().BeFalse();
        document.RootElement.GetProperty("tripId").GetGuid().Should().Be(tripId);
        document.RootElement.GetProperty("returnRouteId").ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement.GetProperty("driverUserId").GetGuid().Should().Be(driverUserId);
        document.RootElement.GetProperty("assistantUserId").GetGuid().Should().Be(assistantUserId);
        document.RootElement.GetProperty("destinationArrivedAt").ValueKind.Should().Be(JsonValueKind.Null);
        mediator.LastRequest.Should().BeOfType<GetTripSnapshotQuery>()
            .Which.Should().Be(new GetTripSnapshotQuery(tripId));
    }

    [Theory]
    [InlineData("2026-07-15T09:30:00+07:00", "2026-07-15T02:30:00Z")]
    [InlineData("2026-07-15T02:30:00Z", "2026-07-15T02:30:00Z")]
    [InlineData("2026-07-15T02:30:00.1234567Z", "2026-07-15T02:30:00.1234567Z")]
    [InlineData("2026-07-15T09:30:00.123+07:00", "2026-07-15T02:30:00.123Z")]
    public async Task GetTrip_WithPricingAt_ForwardsNormalizedUtcInstant(string pricingAt, string expectedUtc)
    {
        var tripId = Guid.NewGuid();
        var snapshot = CreateSnapshot(tripId, Guid.NewGuid(), null);
        var mediator = new StubMediator(_ => snapshot);
        using var factory = new InternalTripsEndpointWebApplicationFactory(mediator);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/internal/v1/trips/{tripId}?pricingAt={Uri.EscapeDataString(pricingAt)}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        mediator.LastRequest.Should().Be(
            new GetTripSnapshotQuery(tripId, DateTimeOffset.Parse(expectedUtc, CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("not-a-timestamp")]
    [InlineData("2026-07-15T09:30:00")]
    [InlineData("07/15/2026T09:30:00Z")]
    [InlineData("2026-07-15T09:30:00 +07:00")]
    public async Task GetTrip_InvalidPricingAt_ReturnsValidationEnvelope(string pricingAt)
    {
        var mediator = new StubMediator(_ => throw new InvalidOperationException("Mediator must not be called."));
        using var factory = new InternalTripsEndpointWebApplicationFactory(mediator);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/internal/v1/trips/{Guid.NewGuid()}?pricingAt={Uri.EscapeDataString(pricingAt)}"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorEnvelopeAsync(response, "VALIDATION_ERROR", hasFields: true);
    }

    [Fact]
    public async Task GetTrip_NotFound_Returns404Envelope()
    {
        var mediator = new StubMediator(_ => throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found."));
        using var factory = new InternalTripsEndpointWebApplicationFactory(mediator);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(CreateAuthorizedRequest(HttpMethod.Get, $"/internal/v1/trips/{Guid.NewGuid()}"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertErrorEnvelopeAsync(response, "TRIP_NOT_FOUND", hasFields: false);
    }

    [Fact]
    public async Task TrackingAuthorization_Happy_ReturnsApiResponseEnvelope()
    {
        var tripId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var mediator = new StubMediator(_ => new TrackingAuthorizationResponse(true, "DRIVER"));
        using var factory = new InternalTripsEndpointWebApplicationFactory(mediator);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/internal/v1/trips/{tripId}/tracking-authorization?userId={userId}&role=DRIVER&operatorId={operatorId}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("data").GetProperty("allowed").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("data").GetProperty("scope").GetString().Should().Be("DRIVER");
        var query = mediator.LastRequest.Should().BeOfType<GetTripTrackingAuthorizationQuery>().Subject;
        query.TripId.Should().Be(tripId);
        query.UserId.Should().Be(userId);
        query.Role.Should().Be("DRIVER");
        query.OperatorId.Should().Be(operatorId);
    }

    [Fact]
    public async Task RouteStops_Happy_ReturnsApiResponseEnvelope()
    {
        var tripId = Guid.NewGuid();
        var stopId = Guid.NewGuid();
        var eta = DateTimeOffset.UtcNow.AddMinutes(30);
        var mediator = new StubMediator(_ => new TripRouteStopsTrackingResponse(
        [
            new TripRouteStopTrackingDto(stopId, 10.75, 106.67, 1, [Guid.NewGuid()], eta),
        ]));
        using var factory = new InternalTripsEndpointWebApplicationFactory(mediator);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(CreateAuthorizedRequest(HttpMethod.Get, $"/internal/v1/trips/{tripId}/route-stops"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var stop = document.RootElement.GetProperty("data").GetProperty("stops")[0];
        stop.GetProperty("stopId").GetGuid().Should().Be(stopId);
        stop.GetProperty("estimatedArrivalTime").GetDateTimeOffset().Should().Be(eta);
        mediator.LastRequest.Should().BeOfType<GetTripRouteStopsTrackingQuery>()
            .Which.TripId.Should().Be(tripId);
    }

    [Fact]
    public async Task RouteGeometry_Happy_ReturnsApiResponseEnvelope()
    {
        var tripId = Guid.NewGuid();
        var mediator = new StubMediator(_ => new TripRouteGeometryTrackingResponse(
            tripId,
            [new RouteGeometryPointDto(10.75, 106.67)],
            [Guid.NewGuid()]));
        using var factory = new InternalTripsEndpointWebApplicationFactory(mediator);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(CreateAuthorizedRequest(HttpMethod.Get, $"/internal/v1/trips/{tripId}/route-geometry"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("data").GetProperty("tripId").GetGuid().Should().Be(tripId);
        document.RootElement.GetProperty("data").GetProperty("points").GetArrayLength().Should().Be(1);
        mediator.LastRequest.Should().BeOfType<GetTripRouteGeometryTrackingQuery>()
            .Which.TripId.Should().Be(tripId);
    }

    [Fact]
    public async Task LockSeats_Happy_ReturnsApiResponseEnvelope()
    {
        var tripId = Guid.NewGuid();
        var seatLockToken = Guid.NewGuid();
        var mediator = new StubMediator(_ => new LockSeatsResult(seatLockToken, ["A01"], DateTimeOffset.UtcNow.AddMinutes(10)));
        using var factory = new InternalTripsEndpointWebApplicationFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Post, $"/internal/v1/trips/{tripId}/lock-seats");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("D"));
        request.Content = JsonContent.Create(new { seatNumbers = new[] { "A01" }, holdOwnerId = Guid.NewGuid(), ttlSeconds = 60 });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("statusCode").GetInt32().Should().Be((int)HttpStatusCode.OK);
        document.RootElement.GetProperty("data").GetProperty("seatLockToken").GetGuid().Should().Be(seatLockToken);
        mediator.LastRequest.Should().BeOfType<LockSeatsCommand>()
            .Which.TripId.Should().Be(tripId);
    }

    [Fact]
    public async Task LockSeats_Unavailable_Returns409EnvelopeWithFields()
    {
        var mediator = new StubMediator(_ => throw SeatUnavailable());
        using var factory = new InternalTripsEndpointWebApplicationFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Post, $"/internal/v1/trips/{Guid.NewGuid()}/lock-seats");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("D"));
        request.Content = JsonContent.Create(new { seatNumbers = new[] { "A01" }, holdOwnerId = Guid.NewGuid(), ttlSeconds = 60 });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertErrorEnvelopeAsync(response, "BOOKING_SEAT_UNAVAILABLE", hasFields: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LockSeats_MissingOrBlankIdempotencyKey_Returns422Envelope(bool blankHeader)
    {
        var mediator = new StubMediator(_ => throw new InvalidOperationException("Mediator should not be called."));
        using var factory = new InternalTripsEndpointWebApplicationFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Post, $"/internal/v1/trips/{Guid.NewGuid()}/lock-seats");
        if (blankHeader)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", " ");
        }

        request.Content = JsonContent.Create(new { seatNumbers = new[] { "A01" }, holdOwnerId = Guid.NewGuid(), ttlSeconds = 60 });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorEnvelopeAsync(response, IdempotencyMiddleware.RequiredErrorCode, hasFields: false);
        mediator.SendCount.Should().Be(0);
    }

    [Fact]
    public async Task LockSeats_IdempotencyMiddleware_ReplaySameKeyReturnsSameSeatLockTokenWithoutSecondSend()
    {
        var redis = InMemoryRedisConnectionMultiplexer.Create();
        var key = Guid.NewGuid().ToString("D");
        var firstToken = Guid.NewGuid();
        var downstreamCalls = 0;
        RequestDelegate next = async context =>
        {
            downstreamCalls++;
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                success = true,
                statusCode = 200,
                data = new { seatLockToken = firstToken },
            });
        };
        var middleware = new IdempotencyMiddleware(
            next,
            redis,
            new IdempotencyOptions { ServicePrefix = "trip", RequireAllMutations = true },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<IdempotencyMiddleware>.Instance);
        var body = JsonSerializer.Serialize(new { seatNumbers = new[] { "A01" }, holdOwnerId = Guid.NewGuid(), ttlSeconds = 60 });
        var firstContext = CreateIdempotencyContext(key, body);
        var secondContext = CreateIdempotencyContext(key, body);

        await middleware.InvokeAsync(firstContext);
        await middleware.InvokeAsync(secondContext);

        downstreamCalls.Should().Be(1);
        ReadResponseJson(firstContext).RootElement.GetProperty("data").GetProperty("seatLockToken").GetGuid().Should().Be(firstToken);
        ReadResponseJson(secondContext).RootElement.GetProperty("data").GetProperty("seatLockToken").GetGuid().Should().Be(firstToken);
    }

    [Fact]
    public async Task LockSeats_IdempotencyMiddleware_ReusedKeyDifferentBodyReturnsIdempotencyKeyMismatchWithoutSecondSend()
    {
        var redis = InMemoryRedisConnectionMultiplexer.Create();
        var key = Guid.NewGuid().ToString("D");
        var downstreamCalls = 0;
        RequestDelegate next = async context =>
        {
            downstreamCalls++;
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            await context.Response.WriteAsync("cached");
        };
        var middleware = new IdempotencyMiddleware(
            next,
            redis,
            new IdempotencyOptions { ServicePrefix = "trip", RequireAllMutations = true },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<IdempotencyMiddleware>.Instance);
        var firstContext = CreateIdempotencyContext(key, "{\"seatNumbers\":[\"A01\"]}");
        var secondContext = CreateIdempotencyContext(key, "{\"seatNumbers\":[\"A02\"]}");

        await middleware.InvokeAsync(firstContext);
        await middleware.InvokeAsync(secondContext);

        downstreamCalls.Should().Be(1);
        secondContext.Response.StatusCode.Should().Be((int)HttpStatusCode.UnprocessableEntity);
        ReadResponseJson(secondContext).RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be(IdempotencyMiddleware.MismatchErrorCode);
    }

    [Fact]
    public async Task RedisSeatLockIdempotencyStore_PendingReservationBlocksConcurrentSameKeyUntilCompleted()
    {
        var redis = InMemoryRedisConnectionMultiplexer.Create();
        var store = new RedisSeatLockIdempotencyStore(redis);
        var tripId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid().ToString("N");
        var fingerprint = "seatNumbers=A01;holdOwnerId=" + Guid.NewGuid().ToString("D") + ";ttlSeconds=60";

        var reserved = await store.TryReserveAsync(
            tripId,
            idempotencyKey,
            fingerprint,
            ["A01"],
            TimeSpan.FromSeconds(60));
        var secondReserved = await store.TryReserveAsync(
            tripId,
            idempotencyKey,
            fingerprint,
            ["A01"],
            TimeSpan.FromSeconds(60));
        var pending = await store.GetAsync(tripId, idempotencyKey);
        var completedResult = new LockSeatsResult(Guid.NewGuid(), ["A01"], DateTimeOffset.UtcNow.AddMinutes(1));

        var completedStored = await store.StoreCompletedAsync(
            tripId,
            idempotencyKey,
            fingerprint,
            reserved.ReservationToken!,
            ["A01"],
            completedResult,
            TimeSpan.FromSeconds(60));
        var completed = await store.GetAsync(tripId, idempotencyKey);

        reserved.Reserved.Should().BeTrue();
        reserved.ReservationToken.Should().NotBeNull();
        completedStored.Should().BeTrue();
        secondReserved.Reserved.Should().BeFalse();
        pending.Should().NotBeNull();
        pending!.IsCompleted.Should().BeFalse();
        pending.RequestFingerprint.Should().Be(fingerprint);
        pending.ReservationToken.Should().NotBeNull();
        completed.Should().NotBeNull();
        completed!.IsCompleted.Should().BeTrue();
        completed.Result.Should().BeEquivalentTo(completedResult);
        completed.ReservationToken.Should().Be(reserved.ReservationToken);
    }

    [Fact]
    public async Task RedisSeatLockIdempotencyStore_StoreCompletedRejectsFingerprintMismatchWithoutOverwrite()
    {
        var redis = InMemoryRedisConnectionMultiplexer.Create();
        var store = new RedisSeatLockIdempotencyStore(redis);
        var tripId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid().ToString("N");
        var originalFingerprint = "seatNumbers=A01;holdOwnerId=" + Guid.NewGuid().ToString("D") + ";ttlSeconds=60";
        var newerFingerprint = "seatNumbers=A02;holdOwnerId=" + Guid.NewGuid().ToString("D") + ";ttlSeconds=60";
        var reservation = await store.TryReserveAsync(tripId, idempotencyKey, originalFingerprint, ["A01"], TimeSpan.FromMinutes(15));
        var original = await store.GetAsync(tripId, idempotencyKey);
        var completedResult = new LockSeatsResult(Guid.NewGuid(), ["A02"], DateTimeOffset.UtcNow.AddMinutes(1));

        var completedStored = await store.StoreCompletedAsync(
            tripId,
            idempotencyKey,
            newerFingerprint,
            reservation.ReservationToken!,
            ["A02"],
            completedResult,
            TimeSpan.FromMinutes(15));
        var current = await store.GetAsync(tripId, idempotencyKey);

        completedStored.Should().BeFalse();
        current.Should().BeEquivalentTo(original);
        current.Should().NotBeNull();
        current!.IsCompleted.Should().BeFalse();
        current.RequestFingerprint.Should().Be(originalFingerprint);
        current.ReservationToken.Should().Be(reservation.ReservationToken);
    }

    [Fact]
    public async Task RedisSeatLockIdempotencyStore_StaleTokenCannotCompleteNewerSameFingerprintReservation()
    {
        var redis = InMemoryRedisConnectionMultiplexer.Create();
        var store = new RedisSeatLockIdempotencyStore(redis);
        var tripId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid().ToString("N");
        var fingerprint = "seatNumbers=A01;holdOwnerId=" + Guid.NewGuid().ToString("D") + ";ttlSeconds=60";
        var staleReservation = await store.TryReserveAsync(tripId, idempotencyKey, fingerprint, ["A01"], TimeSpan.FromMinutes(15));
        await store.RemoveReservationAsync(tripId, idempotencyKey, staleReservation.ReservationToken!, CancellationToken.None);
        var newerReservation = await store.TryReserveAsync(tripId, idempotencyKey, fingerprint, ["A01"], TimeSpan.FromMinutes(15));
        var staleResult = new LockSeatsResult(Guid.NewGuid(), ["A01"], DateTimeOffset.UtcNow.AddMinutes(1));

        var completed = await store.StoreCompletedAsync(
            tripId,
            idempotencyKey,
            fingerprint,
            staleReservation.ReservationToken!,
            ["A01"],
            staleResult,
            TimeSpan.FromMinutes(15));
        var current = await store.GetAsync(tripId, idempotencyKey);

        completed.Should().BeFalse();
        current.Should().NotBeNull();
        current!.IsCompleted.Should().BeFalse();
        current.RequestFingerprint.Should().Be(fingerprint);
        current.ReservationToken.Should().Be(newerReservation.ReservationToken);
    }

    [Fact]
    public async Task RedisSeatLockIdempotencyStore_StaleCleanupDoesNotDeleteNewerReservedEntry()
    {
        var redis = InMemoryRedisConnectionMultiplexer.Create();
        var store = new RedisSeatLockIdempotencyStore(redis);
        var tripId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid().ToString("N");
        var fingerprint = "seatNumbers=A01;holdOwnerId=" + Guid.NewGuid().ToString("D") + ";ttlSeconds=60";
        var staleReservation = await store.TryReserveAsync(tripId, idempotencyKey, fingerprint, ["A01"], TimeSpan.FromMinutes(15));
        await store.RemoveReservationAsync(tripId, idempotencyKey, staleReservation.ReservationToken!, CancellationToken.None);
        var newerReservation = await store.TryReserveAsync(tripId, idempotencyKey, fingerprint, ["A01"], TimeSpan.FromMinutes(15));

        await store.RemoveReservationAsync(tripId, idempotencyKey, staleReservation.ReservationToken!, CancellationToken.None);
        var current = await store.GetAsync(tripId, idempotencyKey);

        current.Should().NotBeNull();
        current!.IsCompleted.Should().BeFalse();
        current.RequestFingerprint.Should().Be(fingerprint);
        current.ReservationToken.Should().Be(newerReservation.ReservationToken);
    }

    [Fact]
    public async Task RedisSeatLockIdempotencyStore_StaleCleanupDoesNotDeleteNewerCompletedEntry()
    {
        var redis = InMemoryRedisConnectionMultiplexer.Create();
        var store = new RedisSeatLockIdempotencyStore(redis);
        var tripId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid().ToString("N");
        var fingerprint = "seatNumbers=A01;holdOwnerId=" + Guid.NewGuid().ToString("D") + ";ttlSeconds=60";
        var staleReservation = await store.TryReserveAsync(tripId, idempotencyKey, fingerprint, ["A01"], TimeSpan.FromMinutes(15));
        await store.RemoveReservationAsync(tripId, idempotencyKey, staleReservation.ReservationToken!, CancellationToken.None);
        var newerReservation = await store.TryReserveAsync(tripId, idempotencyKey, fingerprint, ["A01"], TimeSpan.FromMinutes(15));
        var completedResult = new LockSeatsResult(Guid.NewGuid(), ["A01"], DateTimeOffset.UtcNow.AddMinutes(1));
        await store.StoreCompletedAsync(
            tripId,
            idempotencyKey,
            fingerprint,
            newerReservation.ReservationToken!,
            ["A01"],
            completedResult,
            TimeSpan.FromMinutes(15));

        await store.RemoveReservationAsync(tripId, idempotencyKey, staleReservation.ReservationToken!, CancellationToken.None);

        var current = await store.GetAsync(tripId, idempotencyKey);
        current.Should().NotBeNull();
        current!.IsCompleted.Should().BeTrue();
        current.Result.Should().BeEquivalentTo(completedResult);
        current.ReservationToken.Should().Be(newerReservation.ReservationToken);
    }

    [Fact]
    public async Task ReleaseSeats_Happy_Returns204()
    {
        var tripId = Guid.NewGuid();
        var mediator = new StubMediator(_ => Unit.Value);
        using var factory = new InternalTripsEndpointWebApplicationFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Post, $"/internal/v1/trips/{tripId}/release-seats");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("D"));
        request.Content = JsonContent.Create(new { seatLockToken = Guid.NewGuid(), seatNumbers = new[] { "A01" } });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        mediator.LastRequest.Should().BeOfType<ReleaseSeatsCommand>()
            .Which.TripId.Should().Be(tripId);
    }

    [Fact]
    public async Task ReleaseSeats_AlreadyReleasedNoOp_Returns204()
    {
        var mediator = new StubMediator(_ => Unit.Value);
        using var factory = new InternalTripsEndpointWebApplicationFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Post, $"/internal/v1/trips/{Guid.NewGuid()}/release-seats");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("D"));
        request.Content = JsonContent.Create(new { seatLockToken = Guid.NewGuid(), seatNumbers = new[] { "Z99" } });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task BookSeats_Happy_Returns204()
    {
        var tripId = Guid.NewGuid();
        var mediator = new StubMediator(_ => Unit.Value);
        using var factory = new InternalTripsEndpointWebApplicationFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Post, $"/internal/v1/trips/{tripId}/book-seats");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("D"));
        request.Content = JsonContent.Create(new
        {
            seatLockToken = Guid.NewGuid(),
            bookingId = Guid.NewGuid(),
            passengerSeatAssignments = new[] { new { passengerId = Guid.NewGuid(), seatNumber = "A01" } },
        });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        mediator.LastRequest.Should().BeOfType<BookSeatsCommand>()
            .Which.TripId.Should().Be(tripId);
    }

    [Fact]
    public async Task BookSeats_WrongOrExpiredToken_Returns409EnvelopeWithFields()
    {
        var mediator = new StubMediator(_ => throw SeatUnavailable());
        using var factory = new InternalTripsEndpointWebApplicationFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthorizedRequest(HttpMethod.Post, $"/internal/v1/trips/{Guid.NewGuid()}/book-seats");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("D"));
        request.Content = JsonContent.Create(new
        {
            seatLockToken = Guid.NewGuid(),
            bookingId = Guid.NewGuid(),
            passengerSeatAssignments = new[] { new { passengerId = Guid.NewGuid(), seatNumber = "A01" } },
        });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertErrorEnvelopeAsync(response, "BOOKING_SEAT_UNAVAILABLE", hasFields: true);
    }

    private static DefaultHttpContext CreateIdempotencyContext(string idempotencyKey, string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Headers[IdempotencyMiddleware.IdempotencyKeyHeader] = idempotencyKey;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(),
            "test"));
        return context;
    }

    private static JsonDocument ReadResponseJson(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return JsonDocument.Parse(context.Response.Body);
    }

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt()}");
        return request;
    }

    private static InternalTripSnapshotDto CreateSnapshot(
        Guid tripId,
        Guid? driverUserId = null,
        Guid? assistantUserId = null) => new(
        tripId,
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "SCHEDULED",
        DateTimeOffset.UtcNow.AddHours(1),
        DateTimeOffset.UtcNow.AddHours(3),
        100000,
        new InternalTripStationSnapshotDto(Guid.NewGuid(), "Origin"),
        new InternalTripStationSnapshotDto(Guid.NewGuid(), "Destination"),
        [],
        new InternalTripSeatSummaryDto(1, 1),
        null,
        driverUserId,
        assistantUserId);

    private static CodedConflictException SeatUnavailable() => new(
        "BOOKING_SEAT_UNAVAILABLE",
        "One or more requested seats are unavailable.",
        [new ValidationError("seatNumbers", "A01")]);

    private static async Task AssertErrorEnvelopeAsync(HttpResponseMessage response, string errorCode, bool hasFields)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("statusCode").GetInt32().Should().Be((int)response.StatusCode);
        document.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(errorCode);
        document.RootElement.GetProperty("error").TryGetProperty("fields", out var fields).Should().Be(hasFields);
        if (hasFields)
        {
            fields.GetArrayLength().Should().BeGreaterThan(0);
        }
    }

    private static string CreateInternalJwt()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(InternalTripsWebApplicationFactory.TestSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims: [new Claim("sub", Guid.NewGuid().ToString())],
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class InternalTripsEndpointWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly IMediator mediator;

        public InternalTripsEndpointWebApplicationFactory(IMediator mediator)
        {
            this.mediator = mediator;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("INTERNAL_JWT_SECRET", InternalTripsWebApplicationFactory.TestSecret);
            builder.UseSetting("Trip:BackgroundWorkers:Enabled", "false");
            builder.UseSetting(
                "ConnectionStrings:Default",
                global::VietRide.Trip.IntegrationTests.VietRideWebApplicationFactory.ResolveConnectionString("postgres"));
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMediator>();
                services.AddSingleton(mediator);
            });
        }
    }

    private class InMemoryRedisConnectionMultiplexer : DispatchProxy
    {
        internal static Dictionary<string, RedisValue> Store { get; private set; } = new();

        public static IConnectionMultiplexer Create()
        {
            Store = new Dictionary<string, RedisValue>();
            return DispatchProxy.Create<IConnectionMultiplexer, InMemoryRedisConnectionMultiplexer>()!;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                return null;
            }

            return targetMethod.Name == nameof(IConnectionMultiplexer.GetDatabase)
                ? InMemoryRedisDatabase.Create()
                : targetMethod.ReturnType == typeof(void)
                    ? null
                    : targetMethod.ReturnType.IsValueType
                        ? Activator.CreateInstance(targetMethod.ReturnType)
                        : null;
        }
    }

    private class InMemoryRedisDatabase : DispatchProxy
    {
        public static IDatabase Create()
            => DispatchProxy.Create<IDatabase, InMemoryRedisDatabase>()!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                return null;
            }

            return targetMethod.Name switch
            {
                nameof(IDatabase.StringGetAsync) => Task.FromResult(InMemoryRedisConnectionMultiplexer.Store.TryGetValue(Key(args![0]!), out var value) ? value : RedisValue.Null),
                nameof(IDatabase.StringGet) => InMemoryRedisConnectionMultiplexer.Store.TryGetValue(Key(args![0]!), out var syncValue) ? syncValue : RedisValue.Null,
                nameof(IDatabase.StringSetAsync) => Task.FromResult(Set(Key(args![0]!), (RedisValue)args![1]!, (TimeSpan?)args![2], (When)args![3]!)),
                nameof(IDatabase.StringSet) => Set(Key(args![0]!), (RedisValue)args![1]!, (TimeSpan?)args![2], (When)args![3]!),
                nameof(IDatabase.KeyExistsAsync) => Task.FromResult(InMemoryRedisConnectionMultiplexer.Store.ContainsKey(Key(args![0]!))),
                nameof(IDatabase.KeyExists) => InMemoryRedisConnectionMultiplexer.Store.ContainsKey(Key(args![0]!)),
                nameof(IDatabase.KeyDeleteAsync) => Task.FromResult(Delete(Key(args![0]!))),
                nameof(IDatabase.KeyDelete) => Delete(Key(args![0]!)),
                nameof(IDatabase.ScriptEvaluateAsync) => Task.FromResult(Complete((RedisKey[])args![1]!, (RedisValue[])args![2]!)),
                _ => targetMethod.ReturnType == typeof(void)
                    ? null
                    : targetMethod.ReturnType.IsValueType
                        ? Activator.CreateInstance(targetMethod.ReturnType)
                        : null,
            };
        }

        private static string Key(object key) => key.ToString() ?? string.Empty;

        private static bool Set(string key, RedisValue value, TimeSpan? expiry, When when)
        {
            if (when == When.NotExists && InMemoryRedisConnectionMultiplexer.Store.ContainsKey(key))
            {
                return false;
            }

            InMemoryRedisConnectionMultiplexer.Store[key] = value;
            return true;
        }

        private static RedisResult Complete(RedisKey[] keys, RedisValue[] values)
        {
            var processingKey = Key(keys[0]);
            if (!InMemoryRedisConnectionMultiplexer.Store.TryGetValue(processingKey, out var current))
            {
                return RedisResult.Create((RedisValue)0);
            }

            using var document = JsonDocument.Parse(current.ToString());
            var root = document.RootElement;
            var currentFingerprint = root.TryGetProperty("fingerprint", out var fingerprint)
                ? fingerprint.GetString()
                : root.GetProperty("requestFingerprint").GetString();
            var currentOwnerToken = root.TryGetProperty("ownerToken", out var ownerTokenElement)
                ? ownerTokenElement.GetString()
                : root.GetProperty("reservationToken").GetString();
            if (values.Length == 1)
            {
                if (!string.Equals(currentOwnerToken, values[0].ToString(), StringComparison.Ordinal))
                {
                    return RedisResult.Create((RedisValue)0);
                }

                InMemoryRedisConnectionMultiplexer.Store.Remove(processingKey);
                return RedisResult.Create((RedisValue)1);
            }

            var requestFingerprint = values[0].ToString();
            var ownerToken = values[1].ToString();
            if (!string.Equals(currentFingerprint, requestFingerprint, StringComparison.Ordinal) ||
                !string.Equals(currentOwnerToken, ownerToken, StringComparison.Ordinal))
            {
                return RedisResult.Create((RedisValue)0);
            }

            if (keys.Length == 1)
            {
                InMemoryRedisConnectionMultiplexer.Store[processingKey] = values[2];
            }
            else
            {
                InMemoryRedisConnectionMultiplexer.Store[Key(keys[1])] = values[2];
                InMemoryRedisConnectionMultiplexer.Store.Remove(processingKey);
            }

            return RedisResult.Create((RedisValue)1);
        }

        private static long Delete(string key) => InMemoryRedisConnectionMultiplexer.Store.Remove(key) ? 1L : 0L;
    }

    private sealed class StubMediator : IMediator
    {
        private readonly Func<object, object?> responder;

        public StubMediator(Func<object, object?> responder)
        {
            this.responder = responder;
        }

        public object? LastRequest { get; private set; }
        public int SendCount { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            SendCount++;
            LastRequest = request;
            var response = responder(request);
            return Task.FromResult(response is TResponse typed ? typed : default!);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            SendCount++;
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
}
