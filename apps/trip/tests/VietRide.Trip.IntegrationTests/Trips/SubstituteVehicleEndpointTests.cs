using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
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
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;
using VietRide.Shared.Web.Idempotency;
using VietRide.Trip.Api.Controllers;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.DriverTrips.StartTrip;
using VietRide.Trip.Application.Features.Trips.Operations;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;
using VietRide.Trip.IntegrationTests.Internal.Trips;

namespace VietRide.Trip.IntegrationTests.Trips;

public sealed class SubstituteVehicleEndpointTests
{
    [Fact]
    public async Task InsufficientSeatsRequireExplicitAcknowledgementBeforeAnyWrite()
    {
        await using var harness = await SubstitutionHarness.CreateAsync(insufficientSeats: true);
        var unchanged = await harness.CaptureSnapshotAsync();

        using var rejected = await harness.SendAsync();

        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using (var payload = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync()))
        {
            var error = payload.RootElement.GetProperty("error");
            error.GetProperty("code").GetString()
                .Should().Be("REPLACEMENT_VEHICLE_INSUFFICIENT_SEATS");
            var fields = error.GetProperty("fields").EnumerateArray()
                .ToDictionary(
                    item => item.GetProperty("field").GetString()!,
                    item => item.GetProperty("message").GetString());
            fields.Should().Contain(new Dictionary<string, string?>
            {
                ["usableSeats"] = "3",
                ["passengersToTransfer"] = "4",
                ["missingSeats"] = "1",
            });
        }
        await harness.AssertUnchangedAsync(unchanged);

        using var accepted = await harness.SendAsync(
            acknowledgeInsufficientSeats: true,
            idempotencyKey: Guid.NewGuid().ToString("D"));

        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = harness.OpenDb();
        var acceptedBody = await accepted.Content
            .ReadFromJsonAsync<ApiResponse<SubstituteVehicleResponse>>();
        acceptedBody!.Data!.PendingSeatAssignmentCount.Should().Be(1);
        var audit = await db.TripAuditLogs.AsNoTracking().SingleAsync();
        var metadata = audit.Metadata!.Value;
        metadata.GetProperty("acknowledgedInsufficientSeats").GetBoolean()
            .Should().BeTrue();
        metadata.GetProperty("usableSeats").GetInt32().Should().Be(3);
        metadata.GetProperty("passengersToTransfer").GetInt32().Should().Be(4);
        metadata.GetProperty("missingSeats").GetInt32().Should().Be(1);
        var outbox = await db.OutboxEvents.AsNoTracking()
            .SingleAsync(row => row.EventType == "trip.trip.vehicle_substituted");
        using var eventPayload = JsonDocument.Parse(outbox.Payload);
        var unseated = eventPayload.RootElement.GetProperty("mappings").EnumerateArray()
            .Single(mapping => mapping.GetProperty("newSeatNumber").ValueKind == JsonValueKind.Null);
        unseated.GetProperty("newSeatType").ValueKind.Should().Be(JsonValueKind.Null);
        unseated.GetProperty("isSeatDowngrade").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task SuccessCreatesBoardingReplacementFromVehicleLayoutAndRecoveryTimeline()
    {
        await using var harness = await SubstitutionHarness.CreateAsync();

        using var response = await harness.SendAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<SubstituteVehicleResponse>>();
        body!.Success.Should().BeTrue();
        body.Data!.OldTripStatus.Should().Be("DISRUPTED");
        body.Data.NewTripStatus.Should().Be("BOARDING");
        body.Data.TransferStatus.Should().Be("QUEUED");
        body.Data.AffectedBookingCount.Should().Be(1);
        body.Data.AffectedPassengerCount.Should().Be(2);
        body.Data.PendingSeatAssignmentCount.Should().Be(0);

        await using var assertionDb = harness.OpenDb();
        var oldTrip = await assertionDb.Trips.AsNoTracking().SingleAsync(trip => trip.Id == harness.OldTripId);
        var replacement = await assertionDb.Trips.AsNoTracking()
            .SingleAsync(trip => trip.Id == body.Data.NewTripId);
        oldTrip.Status.Should().Be(TripStatus.DISRUPTED);
        oldTrip.HasSubstitution.Should().BeTrue();
        oldTrip.DisruptedAt.Should().Be(SubstitutionHarness.Now);
        (await assertionDb.Vehicles.AsNoTracking().SingleAsync(vehicle => vehicle.Id == harness.OldVehicleId))
            .Status.Should().Be(VehicleStatus.MAINTENANCE);
        replacement.Status.Should().Be(TripStatus.BOARDING);
        replacement.Source.Should().Be(TripSource.VEHICLE_SUBSTITUTION);
        replacement.DepartureDateTime.Should().Be(SubstitutionHarness.RecoveryDeparture);
        replacement.EstimatedArrivalTime.Should().Be(
            harness.OldEstimatedArrival + (SubstitutionHarness.RecoveryDeparture - SubstitutionHarness.Now));

        var seats = await assertionDb.TripSeats.AsNoTracking()
            .Where(seat => seat.TripId == replacement.Id)
            .OrderBy(seat => seat.SeatNumber)
            .ToArrayAsync();
        seats.Should().HaveCount(3);
        seats.Single(seat => seat.SeatNumber == "B01").Status.Should().Be(TripSeatStatus.BOOKED);
        seats.Single(seat => seat.SeatNumber == "A01").Status.Should().Be(TripSeatStatus.BOOKED);
        seats.Single(seat => seat.SeatNumber == "C01").Status.Should().Be(TripSeatStatus.AVAILABLE);

        var stops = await assertionDb.TripStops.AsNoTracking()
            .Where(stop => stop.TripId == replacement.Id)
            .ToArrayAsync();
        stops.Should().ContainSingle();
        stops[0].StopId.Should().Be(harness.PendingStopId);
        stops[0].EstimatedArrivalTime.Should().Be(
            harness.PendingStopEta + (SubstitutionHarness.RecoveryDeparture - SubstitutionHarness.Now));
        var substitutionAudit = await assertionDb.TripAuditLogs.AsNoTracking()
            .SingleAsync(log => log.TripId == harness.OldTripId
                && log.Action == "VEHICLE_SUBSTITUTION_TRIGGERED");
        substitutionAudit.ActorUserId.Should().Be(harness.ActorId);
        var auditMetadata = substitutionAudit.Metadata!.Value;
        auditMetadata.GetProperty("incidentId").GetGuid().Should().Be(harness.IncidentId);
        auditMetadata.GetProperty("oldVehicleStatusBefore").GetString().Should().Be("ACTIVE");
        auditMetadata.GetProperty("oldVehicleStatusAfter").GetString().Should().Be("MAINTENANCE");
        auditMetadata.GetProperty("oldDriverId").GetGuid().Should().Be(harness.DriverId);
        auditMetadata.GetProperty("newDriverId").GetGuid().Should().Be(harness.ReplacementDriverId);
        auditMetadata.GetProperty("newAssistantId").GetGuid().Should().Be(harness.ReplacementAssistantId);

        var rows = await assertionDb.OutboxEvents.AsNoTracking()
            .Where(row => row.EventType == "trip.trip.vehicle_substituted"
                || row.EventType == "trip.trip.disrupted")
            .ToArrayAsync();
        rows.Should().HaveCount(2).And.OnlyContain(row =>
            row.Status == OutboxEventStatus.PENDING && row.PublishedAt == null);
        rows.Select(row => row.Id).Should().OnlyHaveUniqueItems();
        foreach (var row in rows)
        {
            using var payload = JsonDocument.Parse(row.Payload);
            payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(row.Id);
            if (row.EventType == "trip.trip.vehicle_substituted")
            {
                payload.RootElement.GetProperty("actorUserId").GetGuid()
                    .Should().Be(harness.ActorId);
                payload.RootElement.GetProperty("incidentId").GetGuid()
                    .Should().Be(harness.IncidentId);
                payload.RootElement.GetProperty("newDriverId").GetGuid()
                    .Should().Be(harness.ReplacementDriverId);
            }
        }
    }

    [Fact]
    public async Task StrictContractAuthCrewAndIdempotencyAreEnforced()
    {
        await using var harness = await SubstitutionHarness.CreateAsync();
        var unchanged = await harness.CaptureSnapshotAsync();

        using var whitespace = await harness.SendAsync(reason: "   ");
        whitespace.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorAsync(whitespace, "VALIDATION_ERROR", "reason");
        await harness.AssertUnchangedAsync(unchanged);

        using var overlong = await harness.SendAsync(reason: $"  {new string('x', 501)}  ");
        overlong.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorAsync(overlong, "VALIDATION_ERROR", "reason");
        await harness.AssertUnchangedAsync(unchanged);

        using var missingCrew = await harness.SendRawAsync(
            $$"""
            {
              "replacementVehicleId":"{{harness.ReplacementVehicleId:D}}",
              "incidentId":"{{harness.IncidentId:D}}",
              "estimatedRecoveryDepartureAt":"{{SubstitutionHarness.RecoveryDeparture:O}}",
              "reason":"breakdown",
              "notifyPassengers":true
            }
            """);
        missingCrew.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorAsync(missingCrew, "VALIDATION_ERROR");
        await harness.AssertUnchangedAsync(unchanged);

        using var unknown = await harness.SendRawAsync(
            $$"""
            {
              "replacementVehicleId":"{{harness.ReplacementVehicleId:D}}",
              "estimatedRecoveryDepartureAt":"{{SubstitutionHarness.RecoveryDeparture:O}}",
              "reason":"breakdown",
              "notifyPassengers":true,
              "replacementCrew":null,
              "newVehicleId":"{{Guid.NewGuid():D}}"
            }
            """);
        unknown.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await harness.AssertUnchangedAsync(unchanged);

        using var invalidKey = await harness.SendAsync(idempotencyKey: "not-v4");
        invalidKey.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await harness.AssertUnchangedAsync(unchanged);

        using var missingOffset = await harness.SendRawAsync(
            $$"""
            {
              "replacementVehicleId":"{{harness.ReplacementVehicleId:D}}",
              "estimatedRecoveryDepartureAt":"2026-07-25T09:00:00",
              "reason":"breakdown",
              "notifyPassengers":true,
              "replacementCrew":null
            }
            """);
        missingOffset.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorAsync(missingOffset, "VALIDATION_ERROR");
        await harness.AssertUnchangedAsync(unchanged);

        var replayKey = Guid.NewGuid().ToString("D");
        using var first = await harness.SendAsync(idempotencyKey: replayKey);
        using var replay = await harness.SendAsync(idempotencyKey: replayKey);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadAsStringAsync()).Should()
            .Be(await first.Content.ReadAsStringAsync());
        await using var assertionDb = harness.OpenDb();
        (await assertionDb.Trips.CountAsync()).Should().Be(2);
        (await assertionDb.OutboxEvents.CountAsync()).Should().Be(2);
        (await assertionDb.TripAuditLogs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task InvalidStateTenantVehicleAndCrewLeaveNoPartialMutation()
    {
        await using (var harness = await SubstitutionHarness.CreateAsync())
        {
            var unchanged = await harness.CaptureSnapshotAsync();

            using var unauthenticated = await harness.SendAsync(authenticated: false);
            unauthenticated.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            await harness.AssertUnchangedAsync(unchanged);

            using var foreignTenant = await harness.SendAsync(operatorId: Guid.NewGuid());
            foreignTenant.StatusCode.Should().Be(HttpStatusCode.NotFound);
            await AssertErrorAsync(foreignTenant, "TRIP_NOT_FOUND");
            await harness.AssertUnchangedAsync(unchanged);

            using var missingVehicle = await harness.SendAsync(
                replacementVehicleId: Guid.NewGuid());
            missingVehicle.StatusCode.Should().Be(HttpStatusCode.NotFound);
            await AssertErrorAsync(missingVehicle, "VEHICLE_NOT_FOUND");
            await harness.AssertUnchangedAsync(unchanged);

            using var invalidCrew = await harness.SendAsync(
                replacementDriverId: Guid.NewGuid());
            invalidCrew.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            await AssertErrorAsync(invalidCrew, "VALIDATION_ERROR");
            await harness.AssertUnchangedAsync(unchanged);

            using var sameVehicle = await harness.SendAsync(
                replacementVehicleId: harness.OldVehicleId);
            sameVehicle.StatusCode.Should().Be(HttpStatusCode.Conflict);
            await AssertErrorAsync(sameVehicle, "TRIP_VEHICLE_SAME_AS_OLD");
            await harness.AssertUnchangedAsync(unchanged);

            using var oldCrew = await harness.SendAsync(
                replacementDriverId: harness.DriverId);
            oldCrew.StatusCode.Should().Be(HttpStatusCode.Conflict);
            await AssertErrorAsync(oldCrew, "TRIP_CREW_SAME_AS_OLD");
            await harness.AssertUnchangedAsync(unchanged);

            using var foreignIncident = await harness.SendAsync(incidentId: Guid.NewGuid());
            foreignIncident.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            await AssertErrorAsync(foreignIncident, "VALIDATION_ERROR");
            await harness.AssertUnchangedAsync(unchanged);

            await harness.DeactivateReplacementAsync();
            var inactiveSnapshot = await harness.CaptureSnapshotAsync();
            using var inactiveVehicle = await harness.SendAsync();
            inactiveVehicle.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            await AssertErrorAsync(inactiveVehicle, "VEHICLE_NOT_ACTIVE");
            await harness.AssertUnchangedAsync(inactiveSnapshot);
        }

        await using (var vehicleConflict = await SubstitutionHarness.CreateAsync())
        {
            await vehicleConflict.AddConflictAsync(vehicleConflict: true);
            var unchanged = await vehicleConflict.CaptureSnapshotAsync();
            using var response = await vehicleConflict.SendAsync();
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            await AssertErrorAsync(response, "TRIP_VEHICLE_CONFLICT");
            await vehicleConflict.AssertUnchangedAsync(unchanged);
        }

        await using (var crewConflict = await SubstitutionHarness.CreateAsync())
        {
            await crewConflict.AddConflictAsync(vehicleConflict: false);
            var unchanged = await crewConflict.CaptureSnapshotAsync();
            using var response = await crewConflict.SendAsync();
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            await AssertErrorAsync(response, "TRIP_CREW_CONFLICT");
            await crewConflict.AssertUnchangedAsync(unchanged);
        }
    }

    [Fact]
    public async Task DisruptNoSubstitution_UnknownField_IsRejectedBeforeDispatch()
    {
        await using var harness = await SubstitutionHarness.CreateAsync();
        var unchanged = await harness.CaptureSnapshotAsync();

        using var response = await harness.SendDisruptRawAsync(
            """
            {
              "reason": "Road closure",
              "traveledRatio": 0.5
            }
            """);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorAsync(response, "VALIDATION_ERROR");
        await harness.AssertUnchangedAsync(unchanged);
    }

    [Fact]
    public async Task RejectsRecoveryEqualToOrBeforeLockedDisruptedAtWithExactFieldErrorAndNoWrites()
    {
        await using var harness = await SubstitutionHarness.CreateAsync();
        var unchanged = await harness.CaptureSnapshotAsync();

        using var response = await harness.SendAsync(recoveryDeparture: SubstitutionHarness.Now);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorAsync(response, "VALIDATION_ERROR", "estimatedRecoveryDepartureAt");
        await harness.AssertUnchangedAsync(unchanged);
    }

    [Fact]
    public async Task NonInProgressOldTripReturnsTripNotSubstitutableConflictAndNoWritesWhileLifecycleTripNotInProgressRemainsUnchanged()
    {
        await using var harness = await SubstitutionHarness.CreateAsync(inProgress: false);
        var unchanged = await harness.CaptureSnapshotAsync();

        using var response = await harness.SendAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertErrorAsync(response, "TRIP_NOT_SUBSTITUTABLE");
        await harness.AssertUnchangedAsync(unchanged);
    }

    [Fact]
    public async Task ChainedSubstitutionMapsNullOriginalSeatWithoutBlockingSafetyFlow()
    {
        await using var harness = await SubstitutionHarness.CreateAsync(nullOriginalSeat: true);

        using var response = await harness.SendAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var assertionDb = harness.OpenDb();
        var row = await assertionDb.OutboxEvents.AsNoTracking()
            .SingleAsync(item => item.EventType == "trip.trip.vehicle_substituted");
        using var payload = JsonDocument.Parse(row.Payload);
        var mappings = payload.RootElement.GetProperty("mappings").EnumerateArray().ToArray();
        mappings.Should().ContainSingle();
        mappings[0].GetProperty("originalSeatNumber").ValueKind.Should().Be(JsonValueKind.Null);
        mappings[0].GetProperty("newSeatNumber").GetString().Should().Be("A01");
    }

    [Fact]
    public void ThinControllerDispatchesMediatRAndDeclaresApiResponseAndSwashbuckleMetadata()
    {
        var method = typeof(OperatorTripsController).GetMethod(
            nameof(OperatorTripsController.SubstituteVehicleAsync))!;
        method.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("OPERATOR_ADMIN");
        method.GetCustomAttribute<RequireIdempotencyAttribute>().Should().NotBeNull();
        method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .Should().BeEquivalentTo([200, 403, 404, 409, 422]);
        typeof(OperatorTripsController)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Should().ContainSingle(field => field.FieldType == typeof(IMediator));
    }

    [Fact]
    public async Task ReplacementUsesExistingStartFlowAndCapturesActualDepartureTime()
    {
        await using var harness = await SubstitutionHarness.CreateAsync();
        using var response = await harness.SendAsync();
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<SubstituteVehicleResponse>>();

        await using var db = harness.OpenDb();
        var handler = new StartTripCommandHandler(
            CreateRepository<ITripRepository>(db, "TripRepository"),
            new IntegrationEventOutbox(new OutboxStore(db, new FrozenClock(SubstitutionHarness.RecoveryDeparture))),
            new EfUnitOfWork(db),
            new FrozenClock(SubstitutionHarness.RecoveryDeparture));
        var started = await handler.Handle(
            new StartTripCommand(body!.Data!.NewTripId, harness.DriverId),
            CancellationToken.None);

        started.Status.Should().Be("IN_PROGRESS");
        started.ActualDepartureTime.Should().Be(SubstitutionHarness.RecoveryDeparture);
        db.ChangeTracker.Clear();
        var persisted = await db.Trips.AsNoTracking().SingleAsync(trip => trip.Id == body.Data.NewTripId);
        persisted.Status.Should().Be(TripStatus.IN_PROGRESS);
        persisted.ActualDepartureTime.Should().Be(SubstitutionHarness.RecoveryDeparture);
    }

    private static TRepository CreateRepository<TRepository>(TripDbContext db, string name)
    {
        var type = typeof(TripDbContext).Assembly.GetType(
            $"VietRide.Trip.Infrastructure.Persistence.Repositories.{name}",
            throwOnError: true)!;
        return (TRepository)Activator.CreateInstance(type, db)!;
    }

    private static async Task AssertErrorAsync(
        HttpResponseMessage response,
        string code,
        string? field = null)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(code);
        if (field is not null)
        {
            json.RootElement.GetProperty("error").GetProperty("fields")
                .EnumerateArray()
                .Should().Contain(item => item.GetProperty("field").GetString() == field);
        }
    }

    public sealed class SubstitutionHarness : IAsyncDisposable
    {
        public static readonly DateTimeOffset Now =
            new(2026, 7, 25, 1, 0, 0, TimeSpan.Zero);
        public static readonly DateTimeOffset RecoveryDeparture = Now.AddMinutes(30);
        private const string TestSecret = "test-secret-at-least-32-characters-long";

        private readonly TripDbContext ownerDb;
        private readonly SubstitutionFactory factory;

        private SubstitutionHarness(
            string databaseName,
            TripDbContext ownerDb,
            SubstitutionFactory factory,
            Seed seed)
        {
            DatabaseName = databaseName;
            this.ownerDb = ownerDb;
            this.factory = factory;
            OperatorId = seed.OperatorId;
            OldTripId = seed.OldTripId;
            ReplacementVehicleId = seed.ReplacementVehicleId;
            OldVehicleId = seed.OldVehicleId;
            ActorId = seed.ActorId;
            DriverId = seed.DriverId;
            AssistantId = seed.AssistantId;
            ReplacementDriverId = seed.ReplacementDriverId;
            ReplacementAssistantId = seed.ReplacementAssistantId;
            IncidentId = seed.IncidentId;
            OldEstimatedArrival = seed.OldEstimatedArrival;
            PendingStopId = seed.PendingStopId;
            PendingStopEta = seed.PendingStopEta;
        }

        public string DatabaseName { get; }
        public Guid OperatorId { get; }
        public Guid OldTripId { get; }
        public Guid ReplacementVehicleId { get; }
        public Guid OldVehicleId { get; }
        public Guid ActorId { get; }
        public Guid DriverId { get; }
        public Guid AssistantId { get; }
        public Guid ReplacementDriverId { get; }
        public Guid ReplacementAssistantId { get; }
        public Guid IncidentId { get; }
        public DateTimeOffset OldEstimatedArrival { get; }
        public Guid PendingStopId { get; }
        public DateTimeOffset PendingStopEta { get; }

        public static async Task<SubstitutionHarness> CreateAsync(
            bool inProgress = true,
            bool nullOriginalSeat = false,
            bool insufficientSeats = false,
            bool downgradeVipSeat = false,
            bool upgradeStandardSeat = false)
        {
            var databaseName =
                $"{Day29CargoNearFullOutboxIntegrationTests.ScratchDatabasePrefix}{Guid.NewGuid():N}";
            var db = Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(databaseName);
            await db.Database.MigrateAsync();
            var seed = await SeedAsync(db, inProgress, downgradeVipSeat, upgradeStandardSeat);
            var impact = new ImpactClient(seed, nullOriginalSeat, insufficientSeats);
            var identity = new IdentityClient(seed);
            var factory = new SubstitutionFactory(
                databaseName,
                new FrozenClock(Now),
                impact,
                identity);
            return new SubstitutionHarness(databaseName, db, factory, seed);
        }

        public TripDbContext OpenDb() =>
            Day29CargoNearFullOutboxIntegrationTests.CreateDbContext(DatabaseName);

        public Task<HttpResponseMessage> SendAsync(
            string reason = " Vehicle breakdown ",
            DateTimeOffset? recoveryDeparture = null,
            string? idempotencyKey = null,
            Guid? operatorId = null,
            Guid? actorId = null,
            bool authenticated = true,
            Guid? replacementVehicleId = null,
            Guid? replacementDriverId = null,
            Guid? replacementAssistantId = null,
            Guid? incidentId = null,
            bool acknowledgeInsufficientSeats = false) =>
            SendRawAsync(
                $$"""
                {
                  "replacementVehicleId":"{{(replacementVehicleId ?? ReplacementVehicleId):D}}",
                  "incidentId":"{{(incidentId ?? IncidentId):D}}",
                  "estimatedRecoveryDepartureAt":"{{(recoveryDeparture ?? RecoveryDeparture):O}}",
                  "reason":{{JsonSerializer.Serialize(reason)}},
                  "notifyPassengers":true,
                  "acknowledgeInsufficientSeats":{{acknowledgeInsufficientSeats.ToString().ToLowerInvariant()}},
                  "replacementCrew":{"driverId":"{{(replacementDriverId ?? ReplacementDriverId):D}}","assistantId":"{{(replacementAssistantId ?? ReplacementAssistantId):D}}"}
                }
                """,
                idempotencyKey,
                operatorId,
                actorId,
                authenticated);

        public Task<HttpResponseMessage> SendRawAsync(
            string body,
            string? idempotencyKey = null,
            Guid? operatorId = null,
            Guid? actorId = null,
            bool authenticated = true)
        {
            var client = factory.CreateClient();
            var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/v1/operator/trips/{OldTripId:D}/substitute-vehicle");
            if (authenticated)
            {
                request.Headers.TryAddWithoutValidation(
                    "X-Internal-Auth",
                    $"Bearer {CreateInternalJwt(
                        operatorId ?? OperatorId,
                        actorId ?? ActorId)}");
            }
            request.Headers.TryAddWithoutValidation(
                "Idempotency-Key",
                idempotencyKey ?? Guid.NewGuid().ToString("D"));
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return client.SendAsync(request);
        }

        public async Task<HttpResponseMessage> SendCargoTransferAsync(
            Guid sourceTripId,
            Guid parcelId,
            Guid targetTripId,
            Guid idempotencyKey)
        {
            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/internal/v1/trips/{sourceTripId:D}/cargo/transfer");
            request.Headers.TryAddWithoutValidation(
                "X-Internal-Auth",
                $"Bearer {CreateInternalJwt(OperatorId, ActorId)}");
            request.Headers.TryAddWithoutValidation(
                "Idempotency-Key",
                idempotencyKey.ToString("D"));
            request.Content = JsonContent.Create(new
            {
                parcelId,
                targetTripId,
                targetState = TripCargoParcel.LoadedState,
                allowCapacityOverflow = true,
            });
            return await client.SendAsync(request);
        }

        public Task<HttpResponseMessage> SendDisruptRawAsync(string body)
        {
            var client = factory.CreateClient();
            var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/v1/operator/trips/{OldTripId:D}/disrupt-no-substitution");
            request.Headers.TryAddWithoutValidation(
                "X-Internal-Auth",
                $"Bearer {CreateInternalJwt(OperatorId, ActorId)}");
            request.Headers.TryAddWithoutValidation(
                "Idempotency-Key",
                Guid.NewGuid().ToString("D"));
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return client.SendAsync(request);
        }

        public async Task AssertNoPartialWritesAsync()
        {
            await using var db = OpenDb();
            (await db.Trips.CountAsync()).Should().Be(1);
            (await db.TripAuditLogs.CountAsync()).Should().Be(0);
            (await db.OutboxEvents.CountAsync()).Should().Be(0);
            (await db.TripSeats.CountAsync(seat => seat.TripId != OldTripId)).Should().Be(0);
            (await db.TripStops.CountAsync(stop => stop.TripId != OldTripId)).Should().Be(0);
        }

        public async Task<DatabaseSnapshot> CaptureSnapshotAsync()
        {
            await using var db = OpenDb();
            return new DatabaseSnapshot(
                await db.Trips.AsNoTracking()
                    .OrderBy(trip => trip.Id)
                    .Select(trip => new TripSnapshot(
                        trip.Id,
                        trip.Status,
                        trip.HasSubstitution,
                        trip.DisruptedAt))
                    .ToArrayAsync(),
                await db.TripSeats.AsNoTracking()
                    .OrderBy(seat => seat.Id)
                    .Select(seat => new ChildSnapshot(seat.Id, seat.TripId, seat.Status.ToString()))
                    .ToArrayAsync(),
                await db.TripStops.AsNoTracking()
                    .OrderBy(stop => stop.TripId)
                    .ThenBy(stop => stop.StopId)
                    .Select(stop => new ChildSnapshot(stop.StopId, stop.TripId, stop.Status.ToString()))
                    .ToArrayAsync(),
                await db.TripStopFares.AsNoTracking()
                    .OrderBy(fare => fare.TripId)
                    .ThenBy(fare => fare.StopId)
                    .Select(fare => new ChildSnapshot(fare.StopId, fare.TripId, fare.Source.ToString()))
                    .ToArrayAsync(),
                await db.ResourceReservations.AsNoTracking()
                    .OrderBy(reservation => reservation.Id)
                    .Select(reservation => new ChildSnapshot(
                        reservation.Id,
                        reservation.TripId ?? Guid.Empty,
                        reservation.Status.ToString()))
                    .ToArrayAsync(),
                await db.TripAuditLogs.AsNoTracking().CountAsync(),
                await db.OutboxEvents.AsNoTracking().CountAsync());
        }

        public async Task AssertUnchangedAsync(DatabaseSnapshot before)
        {
            var after = await CaptureSnapshotAsync();
            after.Should().BeEquivalentTo(before, options => options.WithStrictOrdering());
        }

        public async Task DeactivateReplacementAsync()
        {
            await using var db = OpenDb();
            var vehicle = await db.Vehicles.SingleAsync(item => item.Id == ReplacementVehicleId);
            vehicle.Deactivate();
            await db.SaveChangesAsync();
        }

        public async Task AddConflictAsync(bool vehicleConflict)
        {
            await using var db = OpenDb();
            var conflictVehicleId = vehicleConflict ? ReplacementVehicleId : OldVehicleId;
            var conflictVehicle = await db.Vehicles.AsNoTracking()
                .SingleAsync(item => item.Id == conflictVehicleId);
            var conflict = VietRide.Trip.Domain.Entities.Trip.Create(
                OperatorId,
                (await db.Trips.AsNoTracking().SingleAsync(trip => trip.Id == OldTripId)).RouteId,
                conflictVehicleId,
                vehicleConflict ? Guid.NewGuid() : ReplacementDriverId,
                null,
                null,
                RecoveryDeparture,
                RecoveryDeparture.AddHours(2),
                TripSource.MANUAL,
                Money.FromRaw(100_000),
                maxCargoWeightKg: 100m,
                maxCargoVolumeM3: null,
                estimatedPassengerLuggageKg: 10m,
                seatLayoutSnapshotJson: conflictVehicle.SeatLayoutJson);
            db.Trips.Add(conflict);
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            factory.Dispose();
            await Day29CargoNearFullOutboxIntegrationTests.DeleteScratchDatabaseAsync(
                ownerDb,
                DatabaseName);
            await ownerDb.DisposeAsync();
        }

        private static async Task<Seed> SeedAsync(
            TripDbContext db,
            bool inProgress,
            bool downgradeVipSeat,
            bool upgradeStandardSeat)
        {
            var operatorId = Guid.NewGuid();
            var actorId = Guid.NewGuid();
            var driverId = Guid.NewGuid();
            var assistantId = Guid.NewGuid();
            var replacementDriverId = Guid.NewGuid();
            var replacementAssistantId = Guid.NewGuid();
            var origin = Station.Create(
                "Substitution origin",
                $"sub-origin-{Guid.NewGuid():N}",
                "HCMC",
                "HCMC",
                latitude: 10.7626m,
                longitude: 106.6602m);
            var destination = Station.Create(
                "Substitution destination",
                $"sub-destination-{Guid.NewGuid():N}",
                "Da Nang",
                "Da Nang",
                latitude: 16.0544m,
                longitude: 108.2022m);
            var pendingStop = Stop.Create(operatorId, "Pending stop", 10.1m, 106.1m);
            var finalizedStop = Stop.Create(operatorId, "Finalized stop", 10.2m, 106.2m);
            var route = VietRide.Trip.Domain.Entities.Route.Create(
                operatorId,
                "Substitution route",
                origin.Id,
                destination.Id,
                Money.FromRaw(100_000),
                100m,
                240);
            var vehicleType = VehicleType.Create(
                $"SUB_{Guid.NewGuid():N}",
                "Substitution vehicle",
                null,
                3);
            var oldVehicle = Vehicle.Create(
                operatorId,
                vehicleType.Id,
                $"OLD-{Guid.NewGuid():N}"[..20],
                upgradeStandardSeat
                    ? CreateLayout(("A01", "STANDARD"), ("A02", "VIP"))
                    : CreateLayout(("A01", "VIP"), ("A02", "STANDARD")),
                2,
                100m,
                10m);
            var replacement = Vehicle.Create(
                operatorId,
                vehicleType.Id,
                $"NEW-{Guid.NewGuid():N}"[..20],
                downgradeVipSeat
                    ? CreateLayout(("A01", "STANDARD"), ("B01", "STANDARD"), ("C01", "STANDARD"))
                    : upgradeStandardSeat
                        ? CreateLayout(("A01", "VIP"), ("B01", "VIP"), ("C01", "VIP"))
                    : CreateLayout(("A01", "STANDARD"), ("B01", "VIP"), ("C01", "STANDARD")),
                3,
                120m,
                12m);
            var departure = Now.AddHours(-4);
            var oldEstimatedArrival = Now.AddHours(2);
            var trip = VietRide.Trip.Domain.Entities.Trip.Create(
                operatorId,
                route.Id,
                oldVehicle.Id,
                driverId,
                assistantId,
                null,
                departure,
                oldEstimatedArrival,
                TripSource.MANUAL,
                Money.FromRaw(100_000),
                100m,
                10m,
                20m,
                seatLayoutSnapshotJson: oldVehicle.SeatLayoutJson);
            var incident = Incident.Create(
                trip.Id,
                driverId,
                IncidentCategory.VEHICLE_BREAKDOWN,
                "Xe hỏng tại điểm dừng",
                null,
                10.7626m,
                106.6602m,
                Now.AddMinutes(-15));
            if (inProgress)
            {
                trip.MarkBoarding(departure);
                trip.Start(departure);
            }

            var bookedSeat = TripSeat.Create(
                trip.Id,
                "A01",
                upgradeStandardSeat ? TripSeatType.STANDARD : TripSeatType.VIP);
            bookedSeat.MarkHeld();
            bookedSeat.MarkBooked(Guid.NewGuid());
            var otherSeat = TripSeat.Create(
                trip.Id,
                "A02",
                upgradeStandardSeat ? TripSeatType.VIP : TripSeatType.STANDARD);
            var pendingStopEta = Now.AddHours(1);
            var pending = TripStop.Create(
                trip.Id,
                pendingStop.Id,
                1,
                pendingStopEta,
                true,
                true,
                50m);
            var finalized = TripStop.Create(
                trip.Id,
                finalizedStop.Id,
                2,
                Now.AddMinutes(-30),
                true,
                true,
                80m);
            finalized.MarkArrived(Now.AddMinutes(-30));
            var fare = TripStopFare.Create(
                trip.Id,
                pendingStop.Id,
                Money.FromRaw(50_000),
                TripStopFareSource.MANUAL_OVERRIDE);

            db.AddRange(
                origin,
                destination,
                pendingStop,
                finalizedStop,
                route,
                vehicleType,
                oldVehicle,
                replacement,
                trip,
                bookedSeat,
                otherSeat,
                pending,
                finalized,
                fare,
                incident);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return new Seed(
                operatorId,
                trip.Id,
                replacement.Id,
                oldVehicle.Id,
                actorId,
                driverId,
                assistantId,
                replacementDriverId,
                replacementAssistantId,
                incident.Id,
                oldEstimatedArrival,
                pendingStop.Id,
                pendingStopEta,
                Guid.NewGuid(),
                Guid.NewGuid());
        }

        private static JsonElement CreateLayout(params (string Number, string Type)[] seats) =>
            JsonSerializer.SerializeToElement(new
            {
                version = 1,
                vehicleTypeCode = "SUB",
                totalSeats = seats.Length,
                rows = seats.Length,
                cols = 1,
                decks = 1,
                aisles = Array.Empty<object>(),
                seats = seats.Select((seat, index) => new
                {
                    seatNumber = seat.Number,
                    row = index + 1,
                    col = 1,
                    deck = 1,
                    type = seat.Type,
                    isWindow = true,
                    isAisle = false,
                    disabled = false,
                }),
            });

        private static string CreateInternalJwt(Guid operatorId, Guid actorId)
        {
            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret)),
                SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                issuer: "vietride-gateway",
                audience: "vietride-internal",
                claims:
                [
                    new Claim("sub", actorId.ToString()),
                    new Claim(ClaimTypes.Role, "OPERATOR_ADMIN"),
                    new Claim("operatorId", operatorId.ToString()),
                ],
                expires: DateTime.UtcNow.AddMinutes(2),
                signingCredentials: credentials);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private sealed class SubstitutionFactory(
            string databaseName,
            IClock clock,
            IBookingImpactClient impact,
            IIdentityInternalClient identity) : WebApplicationFactory<Program>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseSetting("INTERNAL_JWT_SECRET", TestSecret);
                builder.UseSetting("Identity:BaseUrl", "http://identity.local");
                builder.UseSetting("Trip:BackgroundWorkers:Enabled", "false");
                builder.UseSetting("ConnectionStrings:Default", ConnectionString(databaseName));
                builder.UseSetting("REDIS_URL", "127.0.0.1:6379");
                builder.UseEnvironment("Testing");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IClock>();
                    services.AddSingleton(clock);
                    services.RemoveAll<IBookingImpactClient>();
                    services.AddSingleton(impact);
                    services.RemoveAll<IIdentityInternalClient>();
                    services.AddSingleton(identity);
                });
            }

            private static string ConnectionString(string databaseName)
            {
                const string fallback =
                    "Host=127.0.0.1;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
                var template = Environment.GetEnvironmentVariable(
                    "VIETRIDE_TRIP_TEST_CONNECTION_STRING");
                if (string.IsNullOrWhiteSpace(template))
                {
                    template = fallback;
                }

                return template.Contains("{databaseName}", StringComparison.OrdinalIgnoreCase)
                    ? template.Replace(
                        "{databaseName}",
                        databaseName,
                        StringComparison.OrdinalIgnoreCase)
                    : new Npgsql.NpgsqlConnectionStringBuilder(template)
                    {
                        Database = databaseName,
                    }.ConnectionString;
            }
        }

        private sealed class ImpactClient(
            Seed seed,
            bool nullOriginalSeat,
            bool insufficientSeats) : IBookingImpactClient
        {
            public Task<TripBookingImpactProjection> GetTripEditImpactAsync(
                Guid tripId,
                Guid operatorId,
                CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<VehicleSubstitutionImpactProjection> GetVehicleSubstitutionImpactAsync(
                Guid tripId,
                Guid operatorId,
                CancellationToken cancellationToken)
            {
                tripId.Should().Be(seed.OldTripId);
                operatorId.Should().Be(seed.OperatorId);
                IReadOnlyList<VehicleSubstitutionImpactProjection.Passenger> passengers =
                    insufficientSeats
                    ?
                    [
                        new VehicleSubstitutionImpactProjection.Passenger(seed.FirstPassengerId, "BOARDED", "A01"),
                        new VehicleSubstitutionImpactProjection.Passenger(seed.SecondPassengerId, "PENDING", null),
                        new VehicleSubstitutionImpactProjection.Passenger(
                            Guid.Parse("11111111-1111-4111-8111-111111111111"),
                            "PENDING",
                            null),
                        new VehicleSubstitutionImpactProjection.Passenger(
                            Guid.Parse("22222222-2222-4222-8222-222222222222"),
                            "PENDING",
                            null),
                    ]
                    : nullOriginalSeat
                    ?
                    [
                        new VehicleSubstitutionImpactProjection.Passenger(
                            seed.FirstPassengerId,
                            "PENDING",
                            null),
                    ]
                    :
                    [
                        new VehicleSubstitutionImpactProjection.Passenger(
                            seed.FirstPassengerId,
                            "BOARDED",
                            "A01"),
                        new VehicleSubstitutionImpactProjection.Passenger(
                            seed.SecondPassengerId,
                            "PENDING",
                            null),
                    ];
                return Task.FromResult(new VehicleSubstitutionImpactProjection(
                    seed.OldTripId,
                    seed.OperatorId,
                    [
                        new VehicleSubstitutionImpactProjection.Booking(
                            Guid.NewGuid(),
                            "CONFIRMED",
                            passengers),
                    ]));
            }
        }

        private sealed class IdentityClient(Seed seed) : IIdentityInternalClient
        {
            public Task<OperatorWriteEligibilityValidation> ValidateOperatorCanWriteAsync(
                Guid operatorId,
                CancellationToken cancellationToken = default) =>
                Task.FromResult(OperatorWriteEligibilityValidation.Allowed());

            public Task<IdentityUserLookupResult> GetUserAsync(
                Guid userId,
                CancellationToken cancellationToken = default)
            {
                var role = userId == seed.DriverId
                    ? "DRIVER"
                    : userId == seed.AssistantId
                        ? "ASSISTANT"
                        : userId == seed.ReplacementDriverId
                            ? "DRIVER"
                            : userId == seed.ReplacementAssistantId
                                ? "ASSISTANT"
                        : null;
                return Task.FromResult(role is null
                    ? IdentityUserLookupResult.ValidationFailure("Unknown crew.")
                    : IdentityUserLookupResult.Success(
                        userId,
                        role,
                        seed.OperatorId,
                        "ACTIVE"));
            }
        }

        private sealed record Seed(
            Guid OperatorId,
            Guid OldTripId,
            Guid ReplacementVehicleId,
            Guid OldVehicleId,
            Guid ActorId,
            Guid DriverId,
            Guid AssistantId,
            Guid ReplacementDriverId,
            Guid ReplacementAssistantId,
            Guid IncidentId,
            DateTimeOffset OldEstimatedArrival,
            Guid PendingStopId,
            DateTimeOffset PendingStopEta,
            Guid FirstPassengerId,
            Guid SecondPassengerId);

        public sealed record DatabaseSnapshot(
            IReadOnlyList<TripSnapshot> Trips,
            IReadOnlyList<ChildSnapshot> Seats,
            IReadOnlyList<ChildSnapshot> Stops,
            IReadOnlyList<ChildSnapshot> Fares,
            IReadOnlyList<ChildSnapshot> ResourceReservations,
            int AuditCount,
            int OutboxCount);

        public sealed record TripSnapshot(
            Guid Id,
            TripStatus Status,
            bool HasSubstitution,
            DateTimeOffset? DisruptedAt);

        public sealed record ChildSnapshot(
            Guid Id,
            Guid TripId,
            string State);
    }

    private sealed class FrozenClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
