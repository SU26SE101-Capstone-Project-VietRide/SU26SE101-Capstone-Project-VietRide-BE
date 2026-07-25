using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Shared.Messaging.Outbox;
using VietRide.Shared.Messaging.RabbitMq;
using VietRide.Shared.Persistence;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Trip.Application.Events;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;
using VietRide.Trip.IntegrationTests.Trips;

namespace VietRide.Trip.IntegrationTests.Events;

public sealed class TripVehicleSubstitutedIntegrationEventTests
{
    [Fact]
    public async Task BothFactsAndBusinessMutationAreAtomicWithDistinctCanonicalIdentities()
    {
        await using var harness =
            await SubstituteVehicleEndpointTests.SubstitutionHarness.CreateAsync();

        using var response = await harness.SendAsync();
        response.EnsureSuccessStatusCode();

        await using var db = harness.OpenDb();
        var oldTrip = await db.Trips.AsNoTracking().SingleAsync(trip => trip.Id == harness.OldTripId);
        var replacement = await db.Trips.AsNoTracking()
            .SingleAsync(trip => trip.Source == TripSource.VEHICLE_SUBSTITUTION);
        var rows = await db.OutboxEvents.AsNoTracking()
            .Where(row => row.EventType == TripVehicleSubstitutedIntegrationEvent.EventType
                || row.EventType == "trip.trip.disrupted")
            .OrderBy(row => row.EventType)
            .ToArrayAsync();

        oldTrip.Status.Should().Be(TripStatus.DISRUPTED);
        replacement.Status.Should().Be(TripStatus.BOARDING);
        rows.Should().HaveCount(2);
        rows.Select(row => row.Id).Should().OnlyHaveUniqueItems();
        foreach (var row in rows)
        {
            row.Status.Should().Be(OutboxEventStatus.PENDING);
            row.PublishedAt.Should().BeNull();
            using var payload = JsonDocument.Parse(row.Payload);
            payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(row.Id);
        }
    }

    [Fact]
    public async Task VehicleSubstitutedPublisherRestartPreservesExchangeRoutingKeyMessageIdAndPayload()
    {
        await AssertRestartAsync(TripVehicleSubstitutedIntegrationEvent.EventType);
    }

    [Fact]
    public async Task DisruptedPublisherRestartPreservesExchangeRoutingKeyMessageIdAndPayload()
    {
        await AssertRestartAsync("trip.trip.disrupted");
    }

    [Fact]
    public async Task RollbackRemovesTripsChildrenAuditAndBothOutboxRows()
    {
        await using var harness =
            await SubstituteVehicleEndpointTests.SubstitutionHarness.CreateAsync();
        await using (var setupDb = harness.OpenDb())
        {
            await setupDb.Database.ExecuteSqlRawAsync(
                """
                CREATE OR REPLACE FUNCTION vietride_trip.reject_disrupted_outbox()
                RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.event_type = 'trip.trip.disrupted' THEN
                        RAISE EXCEPTION 'forced disrupted outbox failure';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER reject_disrupted_outbox
                BEFORE INSERT ON vietride_trip.outbox_events
                FOR EACH ROW EXECUTE FUNCTION vietride_trip.reject_disrupted_outbox();
                """);
        }

        using var response = await harness.SendAsync();

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.InternalServerError);
        await harness.AssertNoPartialWritesAsync();
        await using var assertionDb = harness.OpenDb();
        var oldTrip = await assertionDb.Trips.AsNoTracking()
            .SingleAsync(trip => trip.Id == harness.OldTripId);
        oldTrip.Status.Should().Be(TripStatus.IN_PROGRESS);
        oldTrip.HasSubstitution.Should().BeFalse();
    }

    [Fact]
    public async Task SerializedSubstitutionPayloadMatchesSharedContractFieldForField()
    {
        await using var harness =
            await SubstituteVehicleEndpointTests.SubstitutionHarness.CreateAsync(
                nullOriginalSeat: true);
        using var response = await harness.SendAsync();
        response.EnsureSuccessStatusCode();

        await using var db = harness.OpenDb();
        var row = await db.OutboxEvents.AsNoTracking()
            .SingleAsync(item => item.EventType == TripVehicleSubstitutedIntegrationEvent.EventType);
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(
                "eventId",
                "occurredAt",
                "substitutionId",
                "disruptedAt",
                "operatorId",
                "oldTripId",
                "oldTripStatus",
                "oldVehicleId",
                "newTripId",
                "newTripStatus",
                "newVehicleId",
                "newVehiclePlateNumber",
                "newTripDepartureDateTime",
                "actorUserId",
                "reason",
                "notifyPassengers",
                "mappings");
        payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(row.Id);
        payload.RootElement.GetProperty("substitutionId").GetGuid().Should().Be(row.Id);
        payload.RootElement.GetProperty("occurredAt").GetDateTimeOffset()
            .Should().Be(SubstituteVehicleEndpointTests.SubstitutionHarness.Now);
        payload.RootElement.GetProperty("disruptedAt").GetDateTimeOffset()
            .Should().Be(SubstituteVehicleEndpointTests.SubstitutionHarness.Now);
        payload.RootElement.GetProperty("oldTripStatus").GetString().Should().Be("DISRUPTED");
        payload.RootElement.GetProperty("newTripStatus").GetString().Should().Be("BOARDING");
        var mapping = payload.RootElement.GetProperty("mappings").EnumerateArray().Single();
        mapping.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(
                "bookingId",
                "passengerId",
                "originalSeatNumber",
                "newSeatNumber",
                "originalBoardingStatus");
        mapping.GetProperty("originalSeatNumber").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private static async Task AssertRestartAsync(string eventType)
    {
        await using var harness =
            await SubstituteVehicleEndpointTests.SubstitutionHarness.CreateAsync();
        using var response = await harness.SendAsync();
        response.EnsureSuccessStatusCode();

        Guid rowId;
        string payload;
        var failingPublisher = new RecordingPublisher(eventType);
        using (var firstProvider = CreateWorkerProvider(harness, failingPublisher))
        {
            await using var firstStateDb = harness.OpenDb();
            var row = await firstStateDb.OutboxEvents.AsNoTracking()
                .SingleAsync(item => item.EventType == eventType);
            rowId = row.Id;
            payload = row.Payload;

            var worker = CreateWorker(firstProvider);
            (await worker.DrainOnceAsync(CancellationToken.None)).Should().Be(1);
        }

        await using (var failedDb = harness.OpenDb())
        {
            var failed = await failedDb.OutboxEvents.AsNoTracking()
                .SingleAsync(row => row.Id == rowId);
            failed.Status.Should().Be(OutboxEventStatus.FAILED);
            failed.RetryCount.Should().Be(1);
            failed.PublishedAt.Should().BeNull();
        }

        var successfulPublisher = new RecordingPublisher();
        using (var secondProvider = CreateWorkerProvider(harness, successfulPublisher))
        {
            var restartedWorker = CreateWorker(secondProvider);
            (await restartedWorker.DrainOnceAsync(CancellationToken.None)).Should().Be(1);
        }

        failingPublisher.ExchangeName.Should().Be("vietride.events");
        failingPublisher.Deliveries
            .Concat(successfulPublisher.Deliveries)
            .Where(delivery => delivery.RoutingKey == eventType)
            .Should().HaveCount(2).And.OnlyContain(delivery =>
            delivery.RoutingKey == eventType
            && delivery.MessageId == rowId
            && delivery.Payload == payload);
        failingPublisher.Deliveries
            .Concat(successfulPublisher.Deliveries)
            .Where(delivery => delivery.RoutingKey == eventType)
            .Select(delivery => delivery.MessageId)
            .Distinct().Should().ContainSingle();
        await using var completedDb = harness.OpenDb();
        var published = await completedDb.OutboxEvents.AsNoTracking()
            .SingleAsync(row => row.Id == rowId);
        published.Status.Should().Be(OutboxEventStatus.PUBLISHED);
        published.PublishedAt.Should().NotBeNull();
    }

    private static ServiceProvider CreateWorkerProvider(
        SubstituteVehicleEndpointTests.SubstitutionHarness harness,
        IEventPublisher publisher)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock, FrozenClock>();
        services.AddSingleton(publisher);
        services.AddScoped(_ => harness.OpenDb());
        services.AddScoped<VietRideDbContextBase>(
            provider => provider.GetRequiredService<TripDbContext>());
        services.AddScoped<IOutboxStore>(provider => new OutboxStore(
            provider.GetRequiredService<VietRideDbContextBase>(),
            provider.GetRequiredService<IClock>()));
        return services.BuildServiceProvider();
    }

    private static OutboxBackgroundService CreateWorker(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new OutboxOptions
            {
                BatchSize = 50,
                MaxRetryCount = 5,
            }),
            NullLogger<OutboxBackgroundService>.Instance);

    private sealed class RecordingPublisher(string? failOnceFor = null) : IEventPublisher
    {
        private bool failed;
        public string ExchangeName { get; } = new RabbitMqOptions().ExchangeName;
        public List<Delivery> Deliveries { get; } = [];

        public Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct)
            where TEvent : IIntegrationEvent =>
            PublishRawAsync(
                evt.EventType,
                evt.EventId,
                JsonSerializer.Serialize(evt),
                ct);

        public Task PublishRawAsync(
            string routingKey,
            Guid messageId,
            string payloadJson,
            CancellationToken ct)
        {
            Deliveries.Add(new Delivery(routingKey, messageId, payloadJson));
            if (!failed && routingKey == failOnceFor)
            {
                failed = true;
                throw new InvalidOperationException("simulated broker interruption");
            }

            return Task.CompletedTask;
        }
    }

    private sealed record Delivery(string RoutingKey, Guid MessageId, string Payload);

    private sealed class FrozenClock : IClock
    {
        public DateTimeOffset UtcNow =>
            SubstituteVehicleEndpointTests.SubstitutionHarness.Now;
    }
}
