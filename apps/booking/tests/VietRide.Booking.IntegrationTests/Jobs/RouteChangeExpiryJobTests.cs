using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Infrastructure;
using VietRide.Booking.Infrastructure.Jobs;
using VietRide.Booking.IntegrationTests.Messaging;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;

namespace VietRide.Booking.IntegrationTests.Jobs;

public sealed class RouteChangeExpiryJobTests
{
    [Fact]
    public async Task StrictlyAfterDeadlineAppliesFallbackOnceAndKeepsBookingConfirmed()
    {
        var databaseName = $"vr_d33_route_expiry_{Guid.NewGuid():N}";
        var connectionString = Day22EventDatabase.CreateConnectionString(databaseName);
        await Day22EventDatabase.CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = Day22EventDatabase.CreateDataSource(connectionString);
            var deadline = DateTimeOffset.Parse("2026-07-23T01:30:00Z");
            var booking = Day22EventDatabase.CreateBooking(
                Guid.NewGuid(),
                Guid.NewGuid(),
                confirmed: true,
                100_001,
                deadline.AddHours(2));
            var originalStopId = Guid.NewGuid();
            var fallbackDestinationStationId = Guid.NewGuid();
            var action = BookingPendingAction.Create(
                booking.Id,
                BookingPendingActionReason.ROUTE_CHANGE,
                deadline,
                metadata: JsonSerializer.Serialize(new
                {
                    sourceEventId = Guid.NewGuid(),
                    tripId = booking.TripId,
                    operatorId = booking.OperatorId,
                    tripStatus = "IN_PROGRESS",
                    alternativeRouteId = Guid.NewGuid(),
                    deadline,
                    originalStopId,
                    fallbackDestinationStationId,
                    shuttleRequired = true,
                    candidateStops = new[]
                    {
                        new
                        {
                            stopId = (Guid?)Guid.NewGuid(),
                            stationId = (Guid?)null,
                            stationName = "Frozen stop",
                            sequence = 1,
                            estimatedArrivalAt = deadline.AddMinutes(-10),
                        },
                    },
                }));

            await using (var seed = Day22EventDatabase.CreateDbContext(dataSource, deadline))
            {
                await seed.Database.MigrateAsync();
                seed.Bookings.Add(booking);
                seed.BookingPendingActions.Add(action);
                await seed.SaveChangesAsync();
            }

            await ExecuteAsync(dataSource, action.Id, deadline);
            await using (var equality = Day22EventDatabase.CreateDbContext(dataSource, deadline))
            {
                (await equality.BookingPendingActions.AsNoTracking().SingleAsync(row => row.Id == action.Id))
                    .ResolvedAt.Should().BeNull();
                (await equality.OutboxEvents.CountAsync()).Should().Be(0);
            }

            var strictlyAfter = deadline.AddTicks(TimeSpan.TicksPerMicrosecond);
            await ExecuteAsync(dataSource, action.Id, strictlyAfter);
            await ExecuteAsync(dataSource, action.Id, strictlyAfter.AddSeconds(1));

            await using var verify = Day22EventDatabase.CreateDbContext(dataSource, strictlyAfter);
            var persistedBooking = await verify.Bookings.AsNoTracking()
                .SingleAsync(row => row.Id == booking.Id);
            var persistedAction = await verify.BookingPendingActions.AsNoTracking()
                .SingleAsync(row => row.Id == action.Id);
            persistedBooking.Status.Should().Be(BookingStatus.CONFIRMED);
            persistedBooking.CancellationReason.Should().BeNull();
            persistedBooking.RefundOverride.Should().BeFalse();
            persistedAction.ResolvedAction.Should()
                .Be(BookingPendingActionResolved.AUTO_FALLBACK_DESTINATION);
            persistedAction.ResolvedAt.Should().Be(strictlyAfter);
            (await verify.BookingStatusHistories.CountAsync(row => row.BookingId == booking.Id))
                .Should().Be(0);

            var outbox = await verify.OutboxEvents.AsNoTracking().SingleAsync();
            outbox.EventType.Should()
                .Be(BookingRouteChangeAutoFallbackAppliedIntegrationEvent.EventTypeValue);
            outbox.Status.Should().Be(OutboxEventStatus.PENDING);
            outbox.PublishedAt.Should().BeNull();
            using var payload = JsonDocument.Parse(outbox.Payload);
            payload.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
                [
                    "eventId", "occurredAt", "eventType", "bookingId", "tripId", "userId",
                    "pendingActionId", "originalStopId", "fallbackDestinationStationId",
                    "shuttleRequired", "resolvedAction",
                ]);
            payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(outbox.Id);
            payload.RootElement.GetProperty("bookingId").GetGuid().Should().Be(booking.Id);
            payload.RootElement.GetProperty("originalStopId").GetGuid()
                .Should().Be(originalStopId);
            payload.RootElement.GetProperty("fallbackDestinationStationId").GetGuid()
                .Should().Be(fallbackDestinationStationId);
            payload.RootElement.GetProperty("shuttleRequired").GetBoolean().Should().BeTrue();
            payload.RootElement.GetProperty("resolvedAction").GetString()
                .Should().Be("AUTO_FALLBACK_DESTINATION");
            (await verify.OutboxEvents.CountAsync()).Should().Be(1);

            var method = typeof(RouteChangeExpiryJob).GetMethod(nameof(RouteChangeExpiryJob.ExecuteAsync))!;
            method.GetCustomAttribute<QueueAttribute>()!.Queue.Should().Be("booking");
            method.GetCustomAttribute<AutomaticRetryAttribute>()!.Attempts.Should().Be(5);
        }
        finally
        {
            await Day22EventDatabase.DropDatabaseAsync(connectionString, databaseName);
        }
    }

    private static async Task ExecuteAsync(
        Npgsql.NpgsqlDataSource dataSource,
        Guid actionId,
        DateTimeOffset now)
    {
        await using var db = Day22EventDatabase.CreateDbContext(dataSource, now);
        var clock = new FixedClock(now);
        var job = new RouteChangeExpiryJob(
            Day22EventDatabase.CreatePendingActionRepository(db),
            Day22EventDatabase.CreateBookingRepository(db),
            new IntegrationEventOutbox(new OutboxStore(db, clock)),
            new EfUnitOfWork(db),
            clock);
        await job.ExecuteAsync(actionId, CancellationToken.None);
    }
}
