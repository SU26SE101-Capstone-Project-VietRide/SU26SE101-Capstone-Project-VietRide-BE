using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;
using VietRide.Trip.Infrastructure.Messaging;

namespace VietRide.Trip.IntegrationTests.Messaging;

public sealed class Day23BookingCancelledCompatibilityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Consumer_AcceptsCompleteCanonicalAndExactLegacyIdentity()
    {
        var bookingId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        IIntegrationEvent canonical = Deserialize(CanonicalJson(eventId, bookingId));
        IIntegrationEvent legacy = Deserialize(LegacyJson(bookingId));

        canonical.EventId.Should().Be(eventId);
        legacy.EventId.Should().Be(bookingId);
    }

    [Fact]
    public void Consumer_AcceptsOperationalSeatData()
    {
        var bookingId = Guid.NewGuid();
        var tripId = Guid.NewGuid();

        var integrationEvent = Deserialize(OperationalJson(Guid.NewGuid(), bookingId, tripId, "CONFIRMED", ["A01", "A02"]));

        integrationEvent.Invoking(item => item.Validate()).Should().NotThrow();
        integrationEvent.HasOperationalSeatData.Should().BeTrue();
        integrationEvent.TripId.Should().Be(tripId);
        integrationEvent.PreviousStatus.Should().Be("CONFIRMED");
        integrationEvent.SeatNumbers.Should().Equal("A01", "A02");
    }

    [Theory]
    [InlineData("{\"eventId\":\"11111111-1111-1111-1111-111111111111\"}")]
    [InlineData("{\"bookingId\":\"11111111-1111-1111-1111-111111111111\",\"userId\":\"22222222-2222-2222-2222-222222222222\",\"refundAmount\":0,\"refundOverride\":false,\"cancellationReason\":\"USER\",\"unexpected\":true}")]
    public void Consumer_RejectsPartialOrExtraPayloadBeforeDelivery(string json)
    {
        var act = () => Deserialize(json);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public async Task ConsumerHandler_RejectsEmptyIdentityBeforeAccessingPersistence()
    {
        var options = new DbContextOptionsBuilder<TripDbContext>().Options;
        await using var db = new TripDbContext(options, new SystemClock());
        var handler = CreateHandler(db);
        var malformed = new BookingShuttleCancelledIntegrationEvent
        {
            EventId = Guid.Empty,
            OccurredAtOffset = DateTimeOffset.UtcNow,
            BookingId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            RefundAmount = 0,
            RefundOverride = false,
            CancellationReason = "USER",
        };

        var act = () => handler.HandleAsync(malformed, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("{\"eventId\":null,\"bookingId\":\"11111111-1111-1111-1111-111111111111\",\"userId\":\"22222222-2222-2222-2222-222222222222\",\"refundAmount\":0,\"refundOverride\":false,\"cancellationReason\":\"USER\"}")]
    [InlineData("{\"occurredAt\":null,\"bookingId\":\"11111111-1111-1111-1111-111111111111\",\"userId\":\"22222222-2222-2222-2222-222222222222\",\"refundAmount\":0,\"refundOverride\":false,\"cancellationReason\":\"USER\"}")]
    [InlineData("{\"eventId\":null,\"occurredAt\":null,\"bookingId\":\"11111111-1111-1111-1111-111111111111\",\"userId\":\"22222222-2222-2222-2222-222222222222\",\"refundAmount\":0,\"refundOverride\":false,\"cancellationReason\":\"USER\"}")]
    public void Consumer_RejectsExplicitNullIdentityProperties(string json)
    {
        var act = () => Deserialize(json).Validate();

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ConsumerHandler_PersistsCanonicalAndLegacyPayloads_AndDedupesCanonicalRedelivery()
    {
        var databaseName = $"vietride_trip_cancel_compatibility_{Guid.NewGuid():N}";
        await using var db = CreateDbContext(databaseName);
        try
        {
            await db.Database.MigrateAsync();
            var mainTripId = await SeedMainTripAsync(db);
            var canonicalBookingId = Guid.NewGuid();
            var legacyBookingId = Guid.NewGuid();
            db.ShuttlePassengers.AddRange(
                ShuttlePassenger.Request(mainTripId, canonicalBookingId, Guid.NewGuid(), Guid.NewGuid(), "1 Canonical Street", 10m, 106m),
                ShuttlePassenger.Request(mainTripId, legacyBookingId, Guid.NewGuid(), Guid.NewGuid(), "2 Legacy Street", 11m, 107m));
            await db.SaveChangesAsync();

            var canonical = Deserialize(CanonicalJson(Guid.NewGuid(), canonicalBookingId));
            var legacy = Deserialize(LegacyJson(legacyBookingId));
            var handler = CreateHandler(db);

            await handler.HandleAsync(canonical, CancellationToken.None);
            await handler.HandleAsync(legacy, CancellationToken.None);
            await handler.HandleAsync(canonical, CancellationToken.None);

            db.ChangeTracker.Clear();
            var passengers = await db.ShuttlePassengers.AsNoTracking()
                .Where(passenger => passenger.BookingId == canonicalBookingId || passenger.BookingId == legacyBookingId)
                .ToArrayAsync();
            passengers.Should().HaveCount(2);
            passengers.Should().OnlyContain(passenger => passenger.Status == ShuttlePassenger.CancelledStatus);
            passengers.Should().OnlyContain(passenger => passenger.CancelReason == "BOOKING_CANCELLED");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ConsumerHandler_ConfirmedCancellation_ReleasesOnlyOwnedSeats_AndDuplicateIsNoOp()
    {
        var databaseName = $"vietride_trip_cancel_owner_{Guid.NewGuid():N}";
        await using var db = CreateDbContext(databaseName);
        try
        {
            await db.Database.MigrateAsync();
            var tripId = await SeedMainTripAsync(db);
            var cancelledBookingId = Guid.NewGuid();
            var replacementBookingId = Guid.NewGuid();
            var ownedA01 = CreateBookedSeat(tripId, "A01", cancelledBookingId);
            var ownedA02 = CreateBookedSeat(tripId, "A02", cancelledBookingId);
            var replacementA03 = CreateBookedSeat(tripId, "A03", replacementBookingId);
            db.TripSeats.AddRange(ownedA01, ownedA02, replacementA03);
            await db.SaveChangesAsync();

            var integrationEvent = Deserialize(OperationalJson(
                Guid.NewGuid(),
                cancelledBookingId,
                tripId,
                "CONFIRMED",
                ["A01", "A02", "A03"]));
            var handler = CreateHandler(db);

            await handler.HandleAsync(integrationEvent, CancellationToken.None);
            await handler.HandleAsync(integrationEvent, CancellationToken.None);

            db.ChangeTracker.Clear();
            var seats = await db.TripSeats.AsNoTracking()
                .Where(seat => seat.TripId == tripId)
                .OrderBy(seat => seat.SeatNumber)
                .ToArrayAsync();
            seats.Count(seat => seat.Status == TripSeatStatus.AVAILABLE).Should().Be(2);
            seats.Count(seat => seat.Status == TripSeatStatus.BOOKED).Should().Be(1);
            seats[0].Status.Should().Be(TripSeatStatus.AVAILABLE);
            seats[0].BookingId.Should().BeNull();
            seats[1].Status.Should().Be(TripSeatStatus.AVAILABLE);
            seats[1].BookingId.Should().BeNull();
            seats[2].Status.Should().Be(TripSeatStatus.BOOKED);
            seats[2].BookingId.Should().Be(replacementBookingId);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Theory]
    [InlineData("PENDING_PAYMENT", true)]
    [InlineData(null, false)]
    public async Task ConsumerHandler_PendingOrLegacyCancellation_DoesNotReleaseMainTripSeat(
        string? previousStatus,
        bool operational)
    {
        var databaseName = $"vietride_trip_cancel_nonconfirmed_{Guid.NewGuid():N}";
        await using var db = CreateDbContext(databaseName);
        try
        {
            await db.Database.MigrateAsync();
            var tripId = await SeedMainTripAsync(db);
            var bookingId = Guid.NewGuid();
            db.TripSeats.Add(CreateBookedSeat(tripId, "A01", bookingId));
            await db.SaveChangesAsync();
            var integrationEvent = operational
                ? Deserialize(OperationalJson(Guid.NewGuid(), bookingId, tripId, previousStatus!, ["A01"]))
                : Deserialize(LegacyJson(bookingId));

            await CreateHandler(db).HandleAsync(integrationEvent, CancellationToken.None);

            db.ChangeTracker.Clear();
            var seat = await db.TripSeats.AsNoTracking().SingleAsync(item => item.TripId == tripId);
            seat.Status.Should().Be(TripSeatStatus.BOOKED);
            seat.BookingId.Should().Be(bookingId);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static BookingShuttleCancelledIntegrationEvent Deserialize(string json)
        => JsonSerializer.Deserialize<BookingShuttleCancelledIntegrationEvent>(json, JsonOptions)!;

    private static IIntegrationEventHandler<BookingShuttleCancelledIntegrationEvent> CreateHandler(TripDbContext db)
    {
        var type = typeof(BookingShuttleCancelledIntegrationEvent).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Messaging.BookingShuttleCancelledIntegrationEventHandler",
            throwOnError: true)!;
        return (IIntegrationEventHandler<BookingShuttleCancelledIntegrationEvent>)Activator.CreateInstance(
            type,
            db,
            NullLogger<BookingShuttleCancelledIntegrationEvent>.Instance)!;
    }

    private static TripDbContext CreateDbContext(string databaseName)
    {
        var builder = new NpgsqlDataSourceBuilder(CreateConnectionString(databaseName));
        builder.MapEnum<OutboxEventStatus>($"{TripDbContext.SchemaName}.outbox_event_status", new NpgsqlNullNameTranslator());
        TripDbContext.ConfigurePostgresEnums(builder);
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .UseNpgsql(builder.Build(), npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", TripDbContext.SchemaName))
            .ConfigureWarnings(warnings => warnings.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new TripDbContext(options, new SystemClock());
    }

    private static async Task<Guid> SeedMainTripAsync(TripDbContext db)
    {
        var operatorId = Guid.NewGuid();
        var origin = Station.Create("Canonical origin", $"canonical-origin-{Guid.NewGuid():N}", "Ho Chi Minh City", "Ho Chi Minh City");
        var destination = Station.Create("Legacy destination", $"legacy-destination-{Guid.NewGuid():N}", "Da Lat", "Lam Dong");
        var route = Route.Create(operatorId, "Compatibility route", origin.Id, destination.Id, Money.FromRaw(100_000), 300m, 360);
        var vehicleType = VehicleType.Create($"COMPAT_{Guid.NewGuid():N}"[..24], "Compatibility vehicle", 5, 20);
        using var layout = JsonDocument.Parse("{\"rows\":[]}");
        var vehicle = Vehicle.Create(operatorId, vehicleType.Id, $"CMP-{Guid.NewGuid():N}"[..20], layout.RootElement, 20, 500m, 10m);
        var departure = DateTimeOffset.UtcNow.AddHours(4);
        var trip = VietRide.Trip.Domain.Entities.Trip.Create(
            operatorId, route.Id, vehicle.Id, Guid.NewGuid(), null, null, departure, departure.AddHours(3),
            TripSource.MANUAL, Money.FromRaw(100_000), 500m, null, 5m,
            seatLayoutSnapshotJson: vehicle.SeatLayoutJson);

        db.AddRange(origin, destination, route, vehicleType, vehicle, trip);
        await db.SaveChangesAsync();
        return trip.Id;
    }

    private static TripSeat CreateBookedSeat(Guid tripId, string seatNumber, Guid bookingId)
    {
        var seat = TripSeat.Create(tripId, seatNumber);
        seat.MarkHeld();
        seat.MarkBooked(bookingId);
        return seat;
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback = "Host=127.0.0.1;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var template = Environment.GetEnvironmentVariable("VIETRIDE_TRIP_TEST_CONNECTION_STRING");
        template = string.IsNullOrWhiteSpace(template) ? fallback : template;
        return template.Contains("{databaseName}", StringComparison.OrdinalIgnoreCase)
            ? template.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase)
            : template;
    }

    private static string CanonicalJson(Guid eventId, Guid bookingId) => $$"""
        {"eventId":"{{eventId}}","occurredAt":"2026-07-17T00:00:00Z","bookingId":"{{bookingId}}","userId":"33333333-3333-3333-3333-333333333333","refundAmount":0,"refundOverride":false,"cancellationReason":"USER","bookingCode":"VR1","ticketCodes":["T1"],"ticketCount":1}
        """;

    private static string LegacyJson(Guid bookingId) => $$"""
        {"bookingId":"{{bookingId}}","userId":"33333333-3333-3333-3333-333333333333","refundAmount":0,"refundOverride":false,"cancellationReason":"USER"}
        """;

    private static string OperationalJson(
        Guid eventId,
        Guid bookingId,
        Guid tripId,
        string previousStatus,
        IReadOnlyCollection<string> seatNumbers)
        => JsonSerializer.Serialize(new
        {
            eventId,
            occurredAt = "2026-07-17T00:00:00Z",
            bookingId,
            userId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            refundAmount = 0,
            refundOverride = false,
            cancellationReason = "USER",
            bookingCode = "VR1",
            ticketCodes = new[] { "T1" },
            ticketCount = 1,
            tripId,
            previousStatus,
            seatNumbers,
        }, JsonOptions);
}
