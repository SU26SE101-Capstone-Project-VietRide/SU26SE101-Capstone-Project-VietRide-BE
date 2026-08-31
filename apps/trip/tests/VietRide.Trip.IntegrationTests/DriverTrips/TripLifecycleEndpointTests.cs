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
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.DriverTrips.CompleteTrip;
using VietRide.Trip.Application.Features.DriverTrips.StartTrip;
using VietRide.Trip.Application.Features.Trips.StartTripBoarding;
using VietRide.Trip.Application.Services;
using VietRide.Trip.Domain.Constants;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;
using VietRide.Trip.IntegrationTests.TestDoubles;

namespace VietRide.Trip.IntegrationTests.DriverTrips;

public sealed class TripLifecycleEndpointTests
{
    private const string TestSecret = "test-secret-at-least-32-chars-long-xxxxx";

    [Fact]
    public async Task ManualComplete_BeforeDestinationIsRejectedThenSucceedsAfterArrival()
    {
        var databaseName = $"vietride_trip_destination_complete_flow_{Guid.NewGuid():N}";
        var now = DateTimeOffset.Parse("2026-08-31T08:00:00Z");
        await using var setup = CreateDbContext(databaseName);
        try
        {
            await setup.Database.MigrateAsync();
            var trip = await SeedTripAsync(
                setup,
                now,
                inProgress: true,
                destinationArrived: false);
            using var factory = new LifecycleWebApplicationFactory(new DatabaseMediator(databaseName, now));
            using var client = factory.CreateClient();
            var path = $"/v1/driver/trips/{trip.Id}/complete";

            var prematureCompletion = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                path,
                "DRIVER",
                trip.DriverUserId,
                NewKey()));
            await AssertErrorAsync(
                prematureCompletion,
                HttpStatusCode.Conflict,
                "TRIP_DESTINATION_NOT_ARRIVED");

            await using (var rejectedAssertionDb = CreateDbContext(databaseName))
            {
                var unchanged = await rejectedAssertionDb.Trips.SingleAsync(item => item.Id == trip.Id);
                unchanged.Status.Should().Be(TripStatus.IN_PROGRESS);
                unchanged.CompletedAt.Should().BeNull();
                (await rejectedAssertionDb.TripAuditLogs.CountAsync(item => item.TripId == trip.Id))
                    .Should().Be(0);
                (await rejectedAssertionDb.OutboxEvents.CountAsync(item =>
                    item.EventType == "trip.trip.completed")).Should().Be(0);
            }

            await using (var arrivalDb = CreateDbContext(databaseName))
            {
                var arrivedTrip = await arrivalDb.Trips.SingleAsync(item => item.Id == trip.Id);
                arrivedTrip.MarkDestinationArrived(now, trip.DriverUserId);
                await arrivalDb.SaveChangesAsync();
            }

            var completed = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                path,
                "DRIVER",
                trip.DriverUserId,
                NewKey()));
            completed.StatusCode.Should().Be(HttpStatusCode.OK);

            await using var assertionDb = CreateDbContext(databaseName);
            var persisted = await assertionDb.Trips.SingleAsync(item => item.Id == trip.Id);
            persisted.Status.Should().Be(TripStatus.COMPLETED);
            persisted.DestinationArrivedAt.Should().Be(now);
            persisted.CompletedAt.Should().Be(now);
            (await assertionDb.TripAuditLogs.CountAsync(item => item.TripId == trip.Id))
                .Should().Be(1);
            (await assertionDb.OutboxEvents.CountAsync(item =>
                item.EventType == "trip.trip.completed")).Should().Be(1);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ManualBoardingEndpoints_UseInclusiveWindow_NoOpReplay_AndPreserveStrictStartSequence()
    {
        var databaseName = $"vietride_trip_boarding_http_{Guid.NewGuid():N}";
        var now = DateTimeOffset.Parse("2026-08-17T08:00:00Z");
        await using var setup = CreateDbContext(databaseName);
        try
        {
            await setup.Database.MigrateAsync();
            var assigned = await SeedTripAsync(
                setup,
                now,
                inProgress: false,
                scheduled: true,
                departure: now.AddMinutes(180));
            var tooEarly = await SeedTripAsync(
                setup,
                now,
                inProgress: false,
                scheduled: true,
                // PostgreSQL timestamptz stores microsecond precision, so a single 100 ns tick
                // is lost on persistence and becomes the inclusive T-180 boundary.
                departure: now.AddMinutes(180).AddMilliseconds(1));
            using var factory = new LifecycleWebApplicationFactory(new DatabaseMediator(databaseName, now));
            using var client = factory.CreateClient();
            var boardingPath = $"/v1/driver/trips/{assigned.Id}/boarding";

            var directStart = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                $"/v1/driver/trips/{assigned.Id}/start",
                "DRIVER",
                assigned.DriverUserId,
                NewKey()));
            await AssertErrorAsync(directStart, HttpStatusCode.Conflict, "TRIP_INVALID_TRANSITION");

            var assistant = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                boardingPath,
                "ASSISTANT",
                assigned.AssistantUserId!.Value,
                NewKey()));
            assistant.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var wrongDriver = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                boardingPath,
                "DRIVER",
                Guid.NewGuid(),
                NewKey()));
            await AssertErrorAsync(wrongDriver, HttpStatusCode.Forbidden, "FORBIDDEN");

            var early = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                $"/v1/driver/trips/{tooEarly.Id}/boarding",
                "DRIVER",
                tooEarly.DriverUserId,
                NewKey()));
            await AssertErrorAsync(early, HttpStatusCode.Conflict, "TRIP_BOARDING_TOO_EARLY");

            var key = NewKey();
            var boarded = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                boardingPath,
                "DRIVER",
                assigned.DriverUserId,
                key));
            var boardedBytes = await boarded.Content.ReadAsByteArrayAsync();
            boarded.StatusCode.Should().Be(HttpStatusCode.OK);
            AssertBoardingEnvelope(boardedBytes, assigned.Id);

            var replay = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                boardingPath,
                "DRIVER",
                assigned.DriverUserId,
                key));
            replay.StatusCode.Should().Be(HttpStatusCode.OK);
            (await replay.Content.ReadAsByteArrayAsync()).Should().Equal(boardedBytes);

            var newKeyNoOp = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                boardingPath,
                "DRIVER",
                assigned.DriverUserId,
                NewKey()));
            newKeyNoOp.StatusCode.Should().Be(HttpStatusCode.OK);
            AssertBoardingEnvelope(await newKeyNoOp.Content.ReadAsByteArrayAsync(), assigned.Id);

            var started = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                $"/v1/driver/trips/{assigned.Id}/start",
                "DRIVER",
                assigned.DriverUserId,
                NewKey()));
            started.StatusCode.Should().Be(HttpStatusCode.OK);

            await using var assertionDb = CreateDbContext(databaseName);
            var persisted = await assertionDb.Trips.SingleAsync(item => item.Id == assigned.Id);
            persisted.Status.Should().Be(TripStatus.IN_PROGRESS);
            persisted.ActualDepartureTime.Should().Be(now);
            var boardingEvent = await assertionDb.OutboxEvents.SingleAsync(item =>
                item.EventType == "trip.trip.boarding_started");
            AssertBoardingStartedPayload(boardingEvent.Payload, assigned.Id, now);
            var startedEvent = await assertionDb.OutboxEvents.SingleAsync(item =>
                item.EventType == "trip.trip.started");
            AssertStartedPayload(startedEvent.Payload, startedEvent.Id, assigned.Id, now);
            var audit = await assertionDb.TripAuditLogs.SingleAsync(item => item.TripId == assigned.Id);
            audit.Action.Should().Be(TripAuditAction.TripBoardingStartedManual);
            audit.ActorUserId.Should().Be(assigned.DriverUserId);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task OperatorBoarding_IsAdminOnlyAndMasksCrossTenantTrip()
    {
        var databaseName = $"vietride_trip_operator_boarding_http_{Guid.NewGuid():N}";
        var now = DateTimeOffset.Parse("2026-08-17T09:00:00Z");
        await using var setup = CreateDbContext(databaseName);
        try
        {
            await setup.Database.MigrateAsync();
            var trip = await SeedTripAsync(
                setup,
                now,
                inProgress: false,
                scheduled: true,
                departure: now.AddMinutes(120));
            using var factory = new LifecycleWebApplicationFactory(new DatabaseMediator(databaseName, now));
            using var client = factory.CreateClient();
            var path = $"/v1/operator/trips/{trip.Id}/boarding";

            var staff = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                path,
                "OPERATOR_STAFF",
                Guid.NewGuid(),
                NewKey(),
                operatorId: trip.OperatorId));
            staff.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var crossTenant = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                path,
                "OPERATOR_ADMIN",
                Guid.NewGuid(),
                NewKey(),
                operatorId: Guid.NewGuid()));
            await AssertErrorAsync(crossTenant, HttpStatusCode.NotFound, "TRIP_NOT_FOUND");

            var actorId = Guid.NewGuid();
            var success = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                path,
                "OPERATOR_ADMIN",
                actorId,
                NewKey(),
                operatorId: trip.OperatorId));
            success.StatusCode.Should().Be(HttpStatusCode.OK);
            AssertBoardingEnvelope(await success.Content.ReadAsByteArrayAsync(), trip.Id);

            await using var assertionDb = CreateDbContext(databaseName);
            var audit = await assertionDb.TripAuditLogs.SingleAsync(item => item.TripId == trip.Id);
            audit.Action.Should().Be(TripAuditAction.TripBoardingStartedManual);
            audit.ActorUserId.Should().Be(actorId);
            audit.Metadata!.Value.GetProperty("role").GetString().Should().Be("OPERATOR_ADMIN");
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    [Theory]
    [InlineData("start", false)]
    [InlineData("complete", true)]
    public async Task LifecycleEndpoint_WithNonEmptyBody_RejectsWithoutSideEffectsAndAllowsSameKeyEmptyRetry(
        string action,
        bool inProgress)
    {
        var databaseName = $"vietride_trip_{action}_no_body_{Guid.NewGuid():N}";
        var now = DateTimeOffset.Parse("2026-07-14T07:00:00+00:00");
        await using var setup = CreateDbContext(databaseName);
        try
        {
            await setup.Database.MigrateAsync();
            var trip = await SeedTripAsync(setup, now, inProgress);
            using var factory = new LifecycleWebApplicationFactory(new DatabaseMediator(databaseName, now));
            using var client = factory.CreateClient();
            var key = NewKey();
            var path = $"/v1/driver/trips/{trip.Id}/{action}";

            var bodyRequest = CreateRequest(HttpMethod.Post, path, "DRIVER", trip.DriverUserId, key, "{}");
            var rejected = await client.SendAsync(bodyRequest);

            await AssertErrorAsync(rejected, HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");
            await using (var rejectedAssertionDb = CreateDbContext(databaseName))
            {
                var unchanged = await rejectedAssertionDb.Trips.SingleAsync(item => item.Id == trip.Id);
                unchanged.Status.Should().Be(inProgress ? TripStatus.IN_PROGRESS : TripStatus.BOARDING);
                if (!inProgress)
                {
                    unchanged.ActualDepartureTime.Should().BeNull();
                }

                unchanged.CompletedAt.Should().BeNull();
                unchanged.CompletedByUserId.Should().BeNull();
                (await rejectedAssertionDb.OutboxEvents.CountAsync(item =>
                    item.EventType == $"trip.trip.{(action == "start" ? "started" : "completed")}"))
                    .Should().Be(0);
                (await rejectedAssertionDb.TripAuditLogs.CountAsync(item => item.TripId == trip.Id))
                    .Should().Be(0);
            }

            var valid = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                path,
                "DRIVER",
                trip.DriverUserId,
                key));

            valid.StatusCode.Should().Be(HttpStatusCode.OK);
            await using var validAssertionDb = CreateDbContext(databaseName);
            var transitioned = await validAssertionDb.Trips.SingleAsync(item => item.Id == trip.Id);
            transitioned.Status.Should().Be(action == "start" ? TripStatus.IN_PROGRESS : TripStatus.COMPLETED);
            (await validAssertionDb.OutboxEvents.CountAsync(item =>
                item.EventType == $"trip.trip.{(action == "start" ? "started" : "completed")}"))
                .Should().Be(1);
            (await validAssertionDb.TripAuditLogs.CountAsync(item => item.TripId == trip.Id))
                .Should().Be(action == "start" ? 0 : 1);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task StartEndpoint_UsesRealPipelineForAuthorizationEnvelopeReplayAndFingerprintGuards()
    {
        var databaseName = $"vietride_trip_start_http_{Guid.NewGuid():N}";
        var now = DateTimeOffset.Parse("2026-07-14T08:00:00+00:00");
        await using var setup = CreateDbContext(databaseName);
        try
        {
            await setup.Database.MigrateAsync();
            var assigned = await SeedTripAsync(setup, now, inProgress: false);
            var denied = await SeedTripAsync(setup, now, inProgress: false);
            var assistantDenied = await SeedTripAsync(setup, now, inProgress: false);
            using var factory = new LifecycleWebApplicationFactory(new DatabaseMediator(databaseName, now));
            using var client = factory.CreateClient();

            var key = NewKey();
            var first = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                $"/v1/driver/trips/{assigned.Id}/start",
                "DRIVER",
                assigned.DriverUserId,
                key));
            var firstBytes = await first.Content.ReadAsByteArrayAsync();

            first.StatusCode.Should().Be(HttpStatusCode.OK);
            AssertStartEnvelope(firstBytes, assigned.Id, now);

            var replay = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                $"/v1/driver/trips/{assigned.Id}/start",
                "DRIVER",
                assigned.DriverUserId,
                key));
            replay.StatusCode.Should().Be(HttpStatusCode.OK);
            (await replay.Content.ReadAsByteArrayAsync()).Should().Equal(firstBytes);

            var pathMismatch = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                $"/v1/driver/trips/{assigned.Id}/complete",
                "DRIVER",
                assigned.DriverUserId,
                key));
            await AssertErrorAsync(pathMismatch, HttpStatusCode.UnprocessableEntity, "IDEMPOTENCY_KEY_MISMATCH");

            var subjectMismatch = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                $"/v1/driver/trips/{assigned.Id}/start",
                "DRIVER",
                Guid.NewGuid(),
                key));
            await AssertErrorAsync(subjectMismatch, HttpStatusCode.UnprocessableEntity, "IDEMPOTENCY_KEY_MISMATCH");

            var methodMismatch = await client.SendAsync(CreateRequest(
                HttpMethod.Patch,
                $"/v1/driver/trips/{assigned.Id}/start",
                "DRIVER",
                assigned.DriverUserId,
                key));
            await AssertErrorAsync(methodMismatch, HttpStatusCode.UnprocessableEntity, "IDEMPOTENCY_KEY_MISMATCH");

            var invalidTransition = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                $"/v1/driver/trips/{assigned.Id}/start",
                "DRIVER",
                assigned.DriverUserId,
                NewKey()));
            await AssertErrorAsync(invalidTransition, HttpStatusCode.Conflict, "TRIP_INVALID_TRANSITION");

            var wrongDriver = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                $"/v1/driver/trips/{denied.Id}/start",
                "DRIVER",
                Guid.NewGuid(),
                NewKey()));
            await AssertErrorAsync(wrongDriver, HttpStatusCode.Forbidden, "FORBIDDEN");

            var assistant = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                $"/v1/driver/trips/{assistantDenied.Id}/start",
                "ASSISTANT",
                assistantDenied.AssistantUserId!.Value,
                NewKey()));
            assistant.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var missing = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                $"/v1/driver/trips/{denied.Id}/start",
                "DRIVER",
                denied.DriverUserId));
            await AssertErrorAsync(missing, HttpStatusCode.UnprocessableEntity, "IDEMPOTENCY_KEY_REQUIRED");

            var malformed = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                $"/v1/driver/trips/{denied.Id}/start",
                "DRIVER",
                denied.DriverUserId,
                "not-a-uuid-v4"));
            await AssertErrorAsync(malformed, HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");

            await using var assertionDb = CreateDbContext(databaseName);
            var outbox = await assertionDb.OutboxEvents.SingleAsync(item => item.EventType == "trip.trip.started");
            AssertStartedPayload(outbox.Payload, outbox.Id, assigned.Id, now);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    [Theory]
    [InlineData("DRIVER")]
    [InlineData("ASSISTANT")]
    public async Task CompleteEndpoint_PersistsExactEnvelopeEventAndAuditForEachAuthorizedRole(string role)
    {
        var databaseName = $"vietride_trip_complete_http_{role}_{Guid.NewGuid():N}";
        var now = DateTimeOffset.Parse("2026-07-14T09:00:00+00:00");
        await using var setup = CreateDbContext(databaseName);
        try
        {
            await setup.Database.MigrateAsync();
            var trip = await SeedTripAsync(setup, now, inProgress: true);
            var actorId = role == "DRIVER" ? trip.DriverUserId : trip.AssistantUserId!.Value;
            using var factory = new LifecycleWebApplicationFactory(new DatabaseMediator(databaseName, now));
            using var client = factory.CreateClient();

            var key = NewKey();
            var first = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                $"/v1/driver/trips/{trip.Id}/complete",
                role,
                actorId,
                key));

            first.StatusCode.Should().Be(HttpStatusCode.OK);
            var firstBytes = await first.Content.ReadAsByteArrayAsync();
            AssertCompleteEnvelope(firstBytes, trip.Id, actorId, now);

            var replay = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                $"/v1/driver/trips/{trip.Id}/complete",
                role,
                actorId,
                key));
            replay.StatusCode.Should().Be(HttpStatusCode.OK);
            (await replay.Content.ReadAsByteArrayAsync()).Should().Equal(firstBytes);

            var pathMismatch = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                $"/v1/driver/trips/{trip.Id}/start",
                "DRIVER",
                actorId,
                key));
            await AssertErrorAsync(pathMismatch, HttpStatusCode.UnprocessableEntity, "IDEMPOTENCY_KEY_MISMATCH");

            var subjectMismatch = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                $"/v1/driver/trips/{trip.Id}/complete",
                role,
                Guid.NewGuid(),
                key));
            await AssertErrorAsync(subjectMismatch, HttpStatusCode.UnprocessableEntity, "IDEMPOTENCY_KEY_MISMATCH");

            var methodMismatch = await client.SendAsync(CreateRequest(
                HttpMethod.Patch,
                $"/v1/driver/trips/{trip.Id}/complete",
                role,
                actorId,
                key));
            await AssertErrorAsync(methodMismatch, HttpStatusCode.UnprocessableEntity, "IDEMPOTENCY_KEY_MISMATCH");

            await using var assertionDb = CreateDbContext(databaseName);
            var persisted = await assertionDb.Trips.SingleAsync(item => item.Id == trip.Id);
            persisted.Status.Should().Be(TripStatus.COMPLETED);
            persisted.CompletedAt.Should().Be(now);
            persisted.CompletedByUserId.Should().Be(actorId);

            var outbox = await assertionDb.OutboxEvents.SingleAsync(item => item.EventType == "trip.trip.completed");
            AssertCompletedPayload(outbox.Payload, trip.Id, trip.TripCode!, now);
            var audit = await assertionDb.TripAuditLogs.SingleAsync(item => item.TripId == trip.Id);
            AssertManualAudit(audit, trip.Id, actorId, role, now);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task CompleteEndpoint_RejectsRoleAssignmentMismatchAndFreshKeyAfterTransition()
    {
        var databaseName = $"vietride_trip_complete_guard_{Guid.NewGuid():N}";
        var now = DateTimeOffset.Parse("2026-07-14T10:00:00+00:00");
        await using var setup = CreateDbContext(databaseName);
        try
        {
            await setup.Database.MigrateAsync();
            var denied = await SeedTripAsync(setup, now, inProgress: true);
            var completed = await SeedTripAsync(setup, now, inProgress: true);
            using var factory = new LifecycleWebApplicationFactory(new DatabaseMediator(databaseName, now));
            using var client = factory.CreateClient();

            var mismatch = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                $"/v1/driver/trips/{denied.Id}/complete",
                "ASSISTANT",
                Guid.NewGuid(),
                NewKey()));
            await AssertErrorAsync(mismatch, HttpStatusCode.Forbidden, "FORBIDDEN");

            var winner = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                $"/v1/driver/trips/{completed.Id}/complete",
                "DRIVER",
                completed.DriverUserId,
                NewKey()));
            winner.StatusCode.Should().Be(HttpStatusCode.OK);

            var second = await client.SendAsync(CreateRequest(
                HttpMethod.Post,
                $"/v1/driver/trips/{completed.Id}/complete",
                "DRIVER",
                completed.DriverUserId,
                NewKey()));
            await AssertErrorAsync(second, HttpStatusCode.Conflict, "TRIP_INVALID_TRANSITION");
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    [Theory]
    [InlineData("start", "DRIVER")]
    [InlineData("complete", "ASSISTANT")]
    public async Task LifecycleEndpoints_SameKeyWhileFirstRequestIsControlledPending_ReturnsPending(
        string action,
        string role)
    {
        var tripId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-07-14T11:00:00+00:00");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mediator = new DelegatingMediator(async request =>
        {
            entered.TrySetResult();
            await release.Task;
            return request switch
            {
                StartTripCommand => new StartTripResponse(tripId, "IN_PROGRESS", now),
                CompleteTripCommand => new CompleteTripResponse(tripId, "COMPLETED", now, actorId),
                _ => throw new InvalidOperationException("Unexpected request."),
            };
        });
        using var factory = new LifecycleWebApplicationFactory(mediator);
        using var client = factory.CreateClient();
        var key = NewKey();
        var path = $"/v1/driver/trips/{tripId}/{action}";

        var firstTask = client.SendAsync(CreateRequest(HttpMethod.Post, path, role, actorId, key));
        await entered.Task;
        var pending = await client.SendAsync(CreateRequest(HttpMethod.Post, path, role, actorId, key));
        await AssertErrorAsync(pending, HttpStatusCode.Conflict, "IDEMPOTENCY_REQUEST_PENDING");

        release.SetResult();
        var first = await firstTask;
        first.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("start", "DRIVER")]
    [InlineData("complete", "ASSISTANT")]
    public async Task LifecycleEndpoints_MissingOrMalformedKey_ReturnValidationEnvelope(
        string action,
        string role)
    {
        var mediator = new DelegatingMediator(_ =>
            throw new InvalidOperationException("Middleware must reject before dispatch."));
        using var factory = new LifecycleWebApplicationFactory(mediator);
        using var client = factory.CreateClient();
        var path = $"/v1/driver/trips/{Guid.NewGuid()}/{action}";
        var actorId = Guid.NewGuid();

        var missing = await client.SendAsync(CreateRequest(HttpMethod.Post, path, role, actorId));
        await AssertErrorAsync(missing, HttpStatusCode.UnprocessableEntity, "IDEMPOTENCY_KEY_REQUIRED");

        var malformed = await client.SendAsync(CreateRequest(
            HttpMethod.Post,
            path,
            role,
            actorId,
            "not-a-uuid-v4"));
        await AssertErrorAsync(malformed, HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task ConcurrentAuthorizedStartHttpRequests_WithDistinctKeys_ProduceOneWinnerAndExactEvent()
    {
        var databaseName = $"vietride_trip_start_race_{Guid.NewGuid():N}";
        var now = DateTimeOffset.Parse("2026-07-14T12:00:00+00:00");
        await using var setup = CreateDbContext(databaseName);
        try
        {
            await setup.Database.MigrateAsync();
            var trip = await SeedTripAsync(setup, now, inProgress: false);
            using var factory = new LifecycleWebApplicationFactory(new DatabaseMediator(databaseName, now));
            using var client = factory.CreateClient();

            var responses = await Task.WhenAll(
                client.SendAsync(CreateRequest(HttpMethod.Post, $"/v1/driver/trips/{trip.Id}/start", "DRIVER", trip.DriverUserId, NewKey())),
                client.SendAsync(CreateRequest(HttpMethod.Post, $"/v1/driver/trips/{trip.Id}/start", "DRIVER", trip.DriverUserId, NewKey())));

            await AssertOneWinnerOneConflictAsync(responses);
            await using var assertionDb = CreateDbContext(databaseName);
            var persisted = await assertionDb.Trips.SingleAsync(item => item.Id == trip.Id);
            persisted.Status.Should().Be(TripStatus.IN_PROGRESS);
            persisted.ActualDepartureTime.Should().Be(now);
            var outbox = await assertionDb.OutboxEvents.SingleAsync(item => item.EventType == "trip.trip.started");
            AssertStartedPayload(outbox.Payload, outbox.Id, trip.Id, now);
            (await assertionDb.TripAuditLogs.CountAsync(item => item.TripId == trip.Id)).Should().Be(0);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    [Theory]
    [InlineData("DRIVER")]
    [InlineData("ASSISTANT")]
    public async Task ConcurrentAuthorizedCompleteHttpRequests_WithDistinctKeys_ProduceOneWinnerEventAndAudit(
        string role)
    {
        var databaseName = $"vietride_trip_complete_race_{role}_{Guid.NewGuid():N}";
        var now = DateTimeOffset.Parse("2026-07-14T13:00:00+00:00");
        await using var setup = CreateDbContext(databaseName);
        try
        {
            await setup.Database.MigrateAsync();
            var trip = await SeedTripAsync(setup, now, inProgress: true);
            var actorId = role == "DRIVER" ? trip.DriverUserId : trip.AssistantUserId!.Value;
            using var factory = new LifecycleWebApplicationFactory(new DatabaseMediator(databaseName, now));
            using var client = factory.CreateClient();

            var responses = await Task.WhenAll(
                client.SendAsync(CreateRequest(HttpMethod.Post, $"/v1/driver/trips/{trip.Id}/complete", role, actorId, NewKey())),
                client.SendAsync(CreateRequest(HttpMethod.Post, $"/v1/driver/trips/{trip.Id}/complete", role, actorId, NewKey())));

            await AssertOneWinnerOneConflictAsync(responses);
            await using var assertionDb = CreateDbContext(databaseName);
            var persisted = await assertionDb.Trips.SingleAsync(item => item.Id == trip.Id);
            persisted.Status.Should().Be(TripStatus.COMPLETED);
            persisted.CompletedAt.Should().Be(now);
            persisted.CompletedByUserId.Should().Be(actorId);
            var outbox = await assertionDb.OutboxEvents.SingleAsync(item => item.EventType == "trip.trip.completed");
            AssertCompletedPayload(outbox.Payload, trip.Id, trip.TripCode!, now);
            var audit = await assertionDb.TripAuditLogs.SingleAsync(item => item.TripId == trip.Id);
            AssertManualAudit(audit, trip.Id, actorId, role, now);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task LifecycleAcquisition_WaitsForRowLockAndReloadsStaleContenderBeforeTransition()
    {
        var databaseName = $"vietride_trip_lifecycle_{Guid.NewGuid():N}";
        var now = DateTimeOffset.Parse("2026-07-14T14:00:00+00:00");
        await using var setup = CreateDbContext(databaseName);
        try
        {
            await setup.Database.MigrateAsync();
            var seeded = await SeedTripAsync(setup, now, inProgress: false);
            setup.ChangeTracker.Clear();

            await using var lockHolderDb = CreateDbContext(databaseName);
            await using var contenderDb = CreateDbContext(databaseName);
            var lockHolderRepository = CreateRepository(lockHolderDb);
            var contenderRepository = CreateRepository(contenderDb);
            var withoutTransaction = () => contenderRepository.AcquireForLifecycleTransitionAsync(
                Guid.NewGuid(),
                CancellationToken.None);
            await withoutTransaction.Should().ThrowAsync<InvalidOperationException>();

            var staleContender = await contenderDb.Trips.SingleAsync(item => item.Id == seeded.Id);
            staleContender.Status.Should().Be(TripStatus.BOARDING);

            await using var lockHolderTransaction = await lockHolderDb.Database.BeginTransactionAsync();
            await using var contenderTransaction = await contenderDb.Database.BeginTransactionAsync();
            var lockHolder = await lockHolderRepository.AcquireForLifecycleTransitionAsync(
                seeded.Id,
                CancellationToken.None);
            lockHolder.Should().NotBeNull();

            var contenderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var contenderAcquisition = Task.Run(async () =>
            {
                contenderStarted.SetResult();
                return await contenderRepository.AcquireForLifecycleTransitionAsync(
                    seeded.Id,
                    CancellationToken.None);
            });
            await contenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await AssertRemainsBlockedAsync(contenderAcquisition);

            lockHolder!.Start(now);
            await EnqueueStartedEventAsync(lockHolderDb, lockHolder.Id, now);
            await lockHolderDb.SaveChangesAsync();
            await lockHolderTransaction.CommitAsync();

            var reloadedContender = await contenderAcquisition.WaitAsync(TimeSpan.FromSeconds(10));
            reloadedContender.Should().BeSameAs(staleContender);
            reloadedContender!.Status.Should().Be(TripStatus.IN_PROGRESS);
            var rejectedTransition = () => reloadedContender.Start(now.AddMinutes(1));
            rejectedTransition.Should().Throw<InvalidOperationException>();
            await contenderTransaction.RollbackAsync();

            await using var assertionDb = CreateDbContext(databaseName);
            var persisted = await assertionDb.Trips.SingleAsync(item => item.Id == seeded.Id);
            persisted.Status.Should().Be(TripStatus.IN_PROGRESS);
            persisted.ActualDepartureTime.Should().Be(now);
            (await assertionDb.OutboxEvents.CountAsync(item =>
                item.EventType == "trip.trip.started")).Should().Be(1);
            (await assertionDb.TripAuditLogs.CountAsync(item => item.TripId == seeded.Id)).Should().Be(0);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ManualComplete_WhenCommitFailsAfterFlush_RollsBackTripAuditAndEvent()
    {
        var databaseName = $"vietride_trip_complete_rollback_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        await using var setup = CreateDbContext(databaseName);
        try
        {
            await setup.Database.MigrateAsync();
            var trip = await SeedTripAsync(setup, now, inProgress: true);
            await using var executionDb = CreateDbContext(databaseName);
            var unitOfWork = new FailingCommitAfterFlushUnitOfWork(executionDb, trip.Id);
            var handler = new CompleteTripCommandHandler(
                CreateRepository(executionDb),
                CreateAuditRepository(executionDb),
                new IntegrationEventOutbox(new OutboxStore(executionDb, new FrozenClock(now))),
                unitOfWork,
                new FrozenClock(now),
                new ClearParcelImpactClient());

            var action = () => handler.Handle(
                new CompleteTripCommand(trip.Id, trip.DriverUserId, "DRIVER"),
                CancellationToken.None);
            await action.Should().ThrowAsync<InvalidOperationException>();
            unitOfWork.ObservedFlushedTrip.Should().BeTrue();
            unitOfWork.ObservedFlushedAudit.Should().BeTrue();
            unitOfWork.ObservedFlushedOutbox.Should().BeTrue();

            await using var assertionDb = CreateDbContext(databaseName);
            var persisted = await assertionDb.Trips.SingleAsync(item => item.Id == trip.Id);
            persisted.Status.Should().Be(TripStatus.IN_PROGRESS);
            persisted.CompletedAt.Should().BeNull();
            persisted.CompletedByUserId.Should().BeNull();
            (await assertionDb.TripAuditLogs.CountAsync(item => item.TripId == trip.Id)).Should().Be(0);
            (await assertionDb.OutboxEvents.CountAsync(item => item.EventType == "trip.trip.completed")).Should().Be(0);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ManualAndAutomaticCompletionRace_ProducesOneEventAndAuditOnlyWhenManualWins(
        bool manualWins)
    {
        var databaseName = $"vietride_trip_manual_auto_race_{manualWins}_{Guid.NewGuid():N}";
        var now = DateTimeOffset.Parse("2026-07-14T15:00:00+00:00");
        await using var setup = CreateDbContext(databaseName);
        try
        {
            await setup.Database.MigrateAsync();
            var seeded = await SeedTripAsync(setup, now, inProgress: true);
            setup.ChangeTracker.Clear();

            await using var winnerDb = CreateDbContext(databaseName);
            await using var loserDb = CreateDbContext(databaseName);
            var winnerRepository = CreateRepository(winnerDb);
            var loserRepository = CreateRepository(loserDb);
            var staleLoser = await loserDb.Trips.SingleAsync(item => item.Id == seeded.Id);
            staleLoser.Status.Should().Be(TripStatus.IN_PROGRESS);

            await using var winnerTransaction = await winnerDb.Database.BeginTransactionAsync();
            await using var loserTransaction = await loserDb.Database.BeginTransactionAsync();
            var winner = await winnerRepository.AcquireForLifecycleTransitionAsync(
                seeded.Id,
                CancellationToken.None);
            winner.Should().NotBeNull();

            var loserStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var loserAcquisition = Task.Run(async () =>
            {
                loserStarted.SetResult();
                return await loserRepository.AcquireForLifecycleTransitionAsync(
                    seeded.Id,
                    CancellationToken.None);
            });
            await loserStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await AssertRemainsBlockedAsync(loserAcquisition);

            if (manualWins)
            {
                winner!.CompleteManually(now, seeded.DriverUserId);
                await AddManualAuditAsync(winnerDb, seeded, now);
            }
            else
            {
                winner!.CompleteAutomatically(now);
            }

            await EnqueueCompletedEventAsync(winnerDb, winner!.Id, winner.HasSubstitution, now);
            await winnerDb.SaveChangesAsync();
            await winnerTransaction.CommitAsync();

            var loser = await loserAcquisition.WaitAsync(TimeSpan.FromSeconds(10));
            loser.Should().BeSameAs(staleLoser);
            loser!.Status.Should().Be(TripStatus.COMPLETED);
            Action rejectedTransition = manualWins
                ? () => loser.CompleteAutomatically(now.AddMinutes(1))
                : () => loser.CompleteManually(now.AddMinutes(1), seeded.DriverUserId);
            rejectedTransition.Should().Throw<InvalidOperationException>();
            await loserTransaction.RollbackAsync();

            await using var assertionDb = CreateDbContext(databaseName);
            var persisted = await assertionDb.Trips.SingleAsync(item => item.Id == seeded.Id);
            persisted.Status.Should().Be(TripStatus.COMPLETED);
            persisted.CompletedAt.Should().Be(now);
            persisted.CompletedByUserId.Should().Be(manualWins ? seeded.DriverUserId : null);
            (await assertionDb.OutboxEvents.CountAsync(item =>
                item.EventType == "trip.trip.completed")).Should().Be(1);
            (await assertionDb.TripAuditLogs.CountAsync(item => item.TripId == seeded.Id))
                .Should().Be(manualWins ? 1 : 0);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task AssertRemainsBlockedAsync(Task acquisition)
    {
        var timeout = Task.Delay(TimeSpan.FromMilliseconds(300));
        (await Task.WhenAny(acquisition, timeout)).Should().BeSameAs(timeout);
        acquisition.IsCompleted.Should().BeFalse();
    }

    private static Task EnqueueStartedEventAsync(TripDbContext db, Guid tripId, DateTimeOffset now) =>
        new IntegrationEventOutbox(new OutboxStore(db, new FrozenClock(now))).EnqueueAsync(
            "trip.trip.started",
            JsonSerializer.Serialize(new { tripId, actualDepartureTime = now }),
            CancellationToken.None);

    private static Task EnqueueCompletedEventAsync(
        TripDbContext db,
        Guid tripId,
        bool hasSubstitution,
        DateTimeOffset now) =>
        new IntegrationEventOutbox(new OutboxStore(db, new FrozenClock(now))).EnqueueAsync(
            "trip.trip.completed",
            JsonSerializer.Serialize(new { tripId, completedAt = now, hasSubstitution }),
            CancellationToken.None);

    private static Task AddManualAuditAsync(
        TripDbContext db,
        VietRide.Trip.Domain.Entities.Trip trip,
        DateTimeOffset now) =>
        CreateAuditRepository(db).AddAsync(
            TripAuditLog.Create(
                Guid.NewGuid(),
                trip.Id,
                trip.DriverUserId,
                TripAuditAction.TripCompletedManual,
                JsonSerializer.Serialize(new { tripId = trip.Id, role = "DRIVER" }),
                now),
            CancellationToken.None);

    private static async Task AssertOneWinnerOneConflictAsync(HttpResponseMessage[] responses)
    {
        responses.Count(item => item.StatusCode == HttpStatusCode.OK).Should().Be(1);
        var conflict = responses.Single(item => item.StatusCode == HttpStatusCode.Conflict);
        await AssertErrorAsync(conflict, HttpStatusCode.Conflict, "TRIP_INVALID_TRANSITION");
    }

    private static void AssertStartEnvelope(byte[] body, Guid tripId, DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(body);
        AssertSuccessEnvelope(document.RootElement);
        var data = document.RootElement.GetProperty("data");
        data.EnumerateObject().Select(item => item.Name).Should().BeEquivalentTo(
            ["tripId", "status", "actualDepartureTime"]);
        data.GetProperty("tripId").GetGuid().Should().Be(tripId);
        data.GetProperty("status").GetString().Should().Be("IN_PROGRESS");
        data.GetProperty("actualDepartureTime").GetDateTimeOffset().Should().Be(now);
    }

    private static void AssertBoardingEnvelope(byte[] body, Guid tripId)
    {
        using var document = JsonDocument.Parse(body);
        AssertSuccessEnvelope(document.RootElement);
        var data = document.RootElement.GetProperty("data");
        data.EnumerateObject().Select(item => item.Name).Should().BeEquivalentTo(["tripId", "status"]);
        data.GetProperty("tripId").GetGuid().Should().Be(tripId);
        data.GetProperty("status").GetString().Should().Be("BOARDING");
    }

    private static void AssertCompleteEnvelope(byte[] body, Guid tripId, Guid actorId, DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(body);
        AssertSuccessEnvelope(document.RootElement);
        var data = document.RootElement.GetProperty("data");
        data.EnumerateObject().Select(item => item.Name).Should().BeEquivalentTo(
            ["tripId", "status", "completedAt", "completedByUserId"]);
        data.GetProperty("tripId").GetGuid().Should().Be(tripId);
        data.GetProperty("status").GetString().Should().Be("COMPLETED");
        data.GetProperty("completedAt").GetDateTimeOffset().Should().Be(now);
        data.GetProperty("completedByUserId").GetGuid().Should().Be(actorId);
    }

    private static void AssertSuccessEnvelope(JsonElement root)
    {
        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("statusCode").GetInt32().Should().Be(200);
        root.GetProperty("meta").GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private static async Task AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code)
    {
        response.StatusCode.Should().Be(status);
        using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = document.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("statusCode").GetInt32().Should().Be((int)status);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be(code);
        root.GetProperty("meta").GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private static void AssertStartedPayload(
        string payload,
        Guid eventId,
        Guid tripId,
        DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        root.EnumerateObject().Select(item => item.Name).Should().BeEquivalentTo(
            ["eventId", "tripId", "actualDepartureTime"]);
        root.GetProperty("eventId").GetGuid().Should().Be(eventId);
        root.GetProperty("tripId").GetGuid().Should().Be(tripId);
        root.GetProperty("actualDepartureTime").GetDateTimeOffset().Should().Be(now);
    }

    private static void AssertBoardingStartedPayload(
        string payload,
        Guid tripId,
        DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        root.GetProperty("tripId").GetGuid().Should().Be(tripId);
        root.GetProperty("boardingStartedAt").GetDateTimeOffset().Should().Be(now);
    }

    private static void AssertCompletedPayload(
        string payload,
        Guid tripId,
        string tripCode,
        DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        root.EnumerateObject().Select(item => item.Name).Should().BeEquivalentTo(
            [
                "eventId",
                "occurredAt",
                "eventType",
                "tripId",
                "tripCode",
                "operatorId",
                "terminalAt",
                "completedAt",
                "hasSubstitution",
                "source"
            ]);
        root.GetProperty("eventId").GetGuid().Should().NotBeEmpty();
        root.GetProperty("occurredAt").GetDateTime().Should().NotBe(default);
        root.GetProperty("eventType").GetString().Should().Be("trip.trip.completed");
        root.GetProperty("tripId").GetGuid().Should().Be(tripId);
        root.GetProperty("tripCode").GetString().Should().Be(tripCode);
        root.GetProperty("operatorId").GetGuid().Should().NotBeEmpty();
        root.GetProperty("terminalAt").GetDateTimeOffset().Should().Be(now);
        root.GetProperty("completedAt").GetDateTimeOffset().Should().Be(now);
        root.GetProperty("hasSubstitution").GetBoolean().Should().BeFalse();
        root.GetProperty("source").GetString().Should().Be("MANUAL");
    }

    private static void AssertManualAudit(
        TripAuditLog audit,
        Guid tripId,
        Guid actorId,
        string role,
        DateTimeOffset now)
    {
        audit.Action.Should().Be(TripAuditAction.TripCompletedManual);
        audit.ActorUserId.Should().Be(actorId);
        audit.OccurredAt.Should().Be(now);
        audit.Metadata.Should().NotBeNull();
        var metadata = audit.Metadata!.Value;
        metadata.EnumerateObject().Select(item => item.Name).Should().BeEquivalentTo(["tripId", "role"]);
        metadata.GetProperty("tripId").GetGuid().Should().Be(tripId);
        metadata.GetProperty("role").GetString().Should().Be(role);
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path,
        string role,
        Guid subject,
        string? idempotencyKey = null,
        string? body = null,
        Guid? operatorId = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation(
            "X-Internal-Auth",
            $"Bearer {CreateInternalJwt(role, subject, operatorId)}");
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static string CreateInternalJwt(string role, Guid subject, Guid? operatorId = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new("sub", subject.ToString()),
            new(ClaimTypes.Role, role),
        };
        if (operatorId is not null)
        {
            claims.Add(new Claim("operatorId", operatorId.Value.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string NewKey() => Guid.NewGuid().ToString("D");

    private static ITripRepository CreateRepository(TripDbContext db)
    {
        var type = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Persistence.Repositories.TripRepository",
            throwOnError: true)!;
        return (ITripRepository)Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [db],
            culture: null)!;
    }

    private static ITripAuditLogRepository CreateAuditRepository(TripDbContext db)
    {
        var type = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Persistence.Repositories.TripAuditLogRepository",
            throwOnError: true)!;
        return (ITripAuditLogRepository)Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [db],
            culture: null)!;
    }

    private static async Task<VietRide.Trip.Domain.Entities.Trip> SeedTripAsync(
        TripDbContext db,
        DateTimeOffset now,
        bool inProgress,
        bool scheduled = false,
        DateTimeOffset? departure = null,
        bool destinationArrived = true)
    {
        var operatorId = Guid.NewGuid();
        var origin = Station.Create(
            "Lifecycle Origin",
            $"lifecycle-origin-{Guid.NewGuid():N}",
            "Ho Chi Minh City",
            "Ho Chi Minh City",
            latitude: 10.7769m,
            longitude: 106.7009m);
        var destination = Station.Create(
            "Lifecycle Destination",
            $"lifecycle-destination-{Guid.NewGuid():N}",
            "Da Lat",
            "Lam Dong",
            latitude: 11.9404m,
            longitude: 108.4583m);
        var route = VietRide.Trip.Domain.Entities.Route.Create(
            operatorId,
            "Lifecycle route",
            origin.Id,
            destination.Id,
            Money.FromRaw(100_000),
            300m,
            240);
        var vehicleType = VehicleType.Create(
            $"LIFE_{Guid.NewGuid():N}"[..24],
            "Lifecycle test vehicle",
            5,
            20);
        using var layout = JsonDocument.Parse("{\"rows\":[]}");
        var vehicle = Vehicle.Create(
            operatorId,
            vehicleType.Id,
            $"LIFE-{Guid.NewGuid():N}"[..20],
            layout.RootElement,
            20,
            500m,
            10m);
        var departureDateTime = departure ?? now;
        var trip = VietRide.Trip.Domain.Entities.Trip.Create(
            operatorId,
            route.Id,
            vehicle.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            departureDateTime,
            departureDateTime.AddHours(4),
            TripSource.MANUAL,
            Money.FromRaw(100_000),
            500m,
            maxCargoVolumeM3: null,
            estimatedPassengerLuggageKg: 5m,
            seatLayoutSnapshotJson: vehicle.SeatLayoutJson);
        if (!scheduled)
        {
            trip.MarkBoarding(now.AddMinutes(-10));
        }

        if (inProgress)
        {
            trip.Start(now.AddMinutes(-5));
            if (destinationArrived)
            {
                trip.MarkDestinationArrived(now, trip.DriverUserId);
            }
        }

        db.AddRange(origin, destination, route, vehicleType, vehicle, trip);
        await db.SaveChangesAsync();
        return trip;
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

    private sealed class LifecycleWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly IMediator mediator;

        public LifecycleWebApplicationFactory(IMediator mediator) => this.mediator = mediator;

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

    private sealed class DatabaseMediator : IMediator
    {
        private readonly string databaseName;
        private readonly DateTimeOffset now;

        public DatabaseMediator(string databaseName, DateTimeOffset now)
        {
            this.databaseName = databaseName;
            this.now = now;
        }

        public async Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            await using var db = CreateDbContext(databaseName);
            var clock = new FrozenClock(now);
            object response = request switch
            {
                StartTripCommand command => await new StartTripCommandHandler(
                    CreateRepository(db),
                    new IntegrationEventOutbox(new OutboxStore(db, clock)),
                    new DbUnitOfWork(db),
                    clock).Handle(command, cancellationToken),
                StartTripBoardingCommand command => await new StartTripBoardingCommandHandler(
                    new TripBoardingTransitionCoordinator(
                        CreateRepository(db),
                        CreateAuditRepository(db),
                        new IntegrationEventOutbox(new OutboxStore(db, clock)),
                        new DbUnitOfWork(db),
                        new FixedBoardingWindowProvider(TimeSpan.FromMinutes(180))),
                    clock).Handle(command, cancellationToken),
                CompleteTripCommand command => await new CompleteTripCommandHandler(
                    CreateRepository(db),
                    CreateAuditRepository(db),
                    new IntegrationEventOutbox(new OutboxStore(db, clock)),
                    new DbUnitOfWork(db),
                    clock,
                    new ClearParcelImpactClient()).Handle(command, cancellationToken),
                _ => throw new InvalidOperationException($"Unexpected request {request.GetType().Name}."),
            };
            return (TResponse)response;
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => Empty<object?>();
    }

    private sealed class DelegatingMediator : IMediator
    {
        private readonly Func<object, Task<object?>> handler;

        public DelegatingMediator(Func<object, Task<object?>> handler) => this.handler = handler;

        public async Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default) => (TResponse)(await handler(request))!;

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => handler(request);

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => Empty<object?>();
    }

    private static async IAsyncEnumerable<T> Empty<T>()
    {
        await Task.CompletedTask;
        yield break;
    }

    private sealed class FrozenClock : IClock
    {
        public FrozenClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class FixedBoardingWindowProvider(TimeSpan manualEarlyWindow)
        : ITripBoardingWindowProvider
    {
        public TimeSpan ManualEarlyWindow { get; } = manualEarlyWindow;
    }

    private sealed class DbUnitOfWork : IUnitOfWork
    {
        private readonly TripDbContext db;
        private IDbContextTransaction? transaction;

        public DbUnitOfWork(TripDbContext db) => this.db = db;

        public async Task BeginTransactionAsync(CancellationToken cancellationToken = default) =>
            transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            db.SaveChangesAsync(cancellationToken);

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await transaction!.CommitAsync(cancellationToken);
            await transaction.DisposeAsync();
            transaction = null;
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (transaction is null)
            {
                return;
            }

            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();
            transaction = null;
        }

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<Task<T>> operation,
            CancellationToken cancellationToken = default) => operation();
    }

    private sealed class FailingCommitAfterFlushUnitOfWork : IUnitOfWork
    {
        private readonly TripDbContext db;
        private readonly Guid tripId;
        private IDbContextTransaction? transaction;

        public FailingCommitAfterFlushUnitOfWork(TripDbContext db, Guid tripId)
        {
            this.db = db;
            this.tripId = tripId;
        }

        public bool ObservedFlushedTrip { get; private set; }
        public bool ObservedFlushedAudit { get; private set; }
        public bool ObservedFlushedOutbox { get; private set; }

        public async Task BeginTransactionAsync(CancellationToken cancellationToken = default) =>
            transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            db.SaveChangesAsync(cancellationToken);

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            var flushedTrip = await db.Trips.SingleAsync(item => item.Id == tripId, cancellationToken);
            ObservedFlushedTrip = flushedTrip.Status == TripStatus.COMPLETED;
            ObservedFlushedAudit = await db.TripAuditLogs.AnyAsync(
                item => item.TripId == tripId,
                cancellationToken);
            ObservedFlushedOutbox = await db.OutboxEvents.AnyAsync(
                item => item.EventType == "trip.trip.completed",
                cancellationToken);
            throw new InvalidOperationException("Controlled failure after flush and before commit.");
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (transaction is null)
            {
                return;
            }

            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();
            transaction = null;
        }

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<Task<T>> operation,
            CancellationToken cancellationToken = default) => operation();
    }
}
