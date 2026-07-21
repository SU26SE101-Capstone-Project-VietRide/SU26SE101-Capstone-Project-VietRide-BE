using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Bookings.HandleStopDisabled;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Booking.Infrastructure;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests.StopDisabled;

[Collection(VoucherPersistenceCollection.CollectionName)]
public sealed class Day24StopDisabledConsumerIntegrationTests
    : IClassFixture<VoucherPersistenceIntegrationTests.DbBackedVoucherFactory>
{
    private readonly VoucherPersistenceIntegrationTests.DbBackedVoucherFactory _factory;

    public Day24StopDisabledConsumerIntegrationTests(
        VoucherPersistenceIntegrationTests.DbBackedVoucherFactory factory) => _factory = factory;

    [Fact]
    public async Task ConsumerPersistsActionAndOutboxAtomicallyAndDedupesEventIdentity()
    {
        await _factory.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var operatorId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var stopId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(now), Guid.NewGuid(), Guid.NewGuid(), operatorId,
            null, stopId, null, null, Money.FromRaw(100_000), Money.Zero, Money.FromRaw(100_000),
            tripSnapshotDeparture: now.AddHours(2));
        booking.Confirm(now.AddHours(-1));
        var pendingPayment = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(now.AddMinutes(1)), Guid.NewGuid(), booking.TripId, operatorId,
            null, stopId, null, null, Money.FromRaw(100_000), Money.Zero, Money.FromRaw(100_000),
            tripSnapshotDeparture: now.AddHours(2));

        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var seed = seedScope.ServiceProvider.GetRequiredService<BookingDbContext>();
            seed.Bookings.AddRange(booking, pendingPayment);
            await seed.SaveChangesAsync();
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            var tripClient = Substitute.For<ITripServiceClient>();
            var originStationId = Guid.NewGuid();
            tripClient.GetTripSnapshotAsync(booking.TripId, Arg.Any<CancellationToken>())
                .Returns(new TripSnapshot(
                    booking.TripId, operatorId, Guid.NewGuid(), Guid.NewGuid(), "BOARDING",
                    now.AddHours(2), now.AddHours(3), 100_000,
                    new TripStationSnapshot(originStationId, "Origin"),
                    new TripStationSnapshot(Guid.NewGuid(), "Destination"), [], new TripSeatSummary(10, 10)));
            var handler = new HandleStopDisabledCommandHandler(
                scope.ServiceProvider.GetRequiredService<IBookingRepository>(),
                scope.ServiceProvider.GetRequiredService<IBookingPendingActionRepository>(),
                scope.ServiceProvider.GetRequiredService<IBookingStatsRepository>(),
                new IntegrationEventOutbox(scope.ServiceProvider.GetRequiredService<IOutboxStore>()),
                new EfUnitOfWork(db),
                scope.ServiceProvider.GetRequiredService<VietRide.Shared.Kernel.Abstractions.IClock>(),
                tripClient);
            (await handler.Handle(new HandleStopDisabledCommand(eventId, stopId, operatorId, null), default))
                .Should().Be(1);
            (await handler.Handle(new HandleStopDisabledCommand(eventId, stopId, operatorId, null), default))
                .Should().Be(0);
        }

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var action = await verify.BookingPendingActions.AsNoTracking()
            .SingleAsync(row => row.BookingId == booking.Id);
        action.Reason.Should().Be(BookingPendingActionReason.STOP_DISABLED);
        action.Severity.Should().BeNull();
        action.Deadline.Should().BeCloseTo(booking.TripCurrentDeparture!.Value.AddHours(-2), TimeSpan.FromMicroseconds(1));
        using var metadata = JsonDocument.Parse(action.Metadata!);
        metadata.RootElement.GetProperty("disabledStopId").GetGuid().Should().Be(stopId);
        metadata.RootElement.GetProperty("affectedField").GetString().Should().Be("PICKUP");
        metadata.RootElement.GetProperty("fallbackStationId").GetGuid().Should().NotBe(Guid.Empty);
        metadata.RootElement.TryGetProperty("suggestedStopId", out _).Should().BeFalse();
        var outbox = await verify.OutboxEvents.AsNoTracking()
            .SingleAsync(row => row.Id == eventId);
        outbox.Id.Should().Be(eventId);
        outbox.Status.Should().Be(OutboxEventStatus.PENDING);
        outbox.PublishedAt.Should().BeNull();
        using var payload = JsonDocument.Parse(outbox.Payload);
        payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(eventId);
        payload.RootElement.GetProperty("occurredAt").GetDateTime().Should().NotBe(default);
        payload.RootElement.GetProperty("eventType").GetString().Should().Be("booking.stop_disabled.affected");
        payload.RootElement.GetProperty("stopId").GetGuid().Should().Be(stopId);
        payload.RootElement.GetProperty("recipientUserIds").GetArrayLength().Should().Be(1);
        payload.RootElement.GetProperty("affectedBookingCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ConsumerAcceptsScheduledTripAndExcludesPendingPaymentBooking()
    {
        await _factory.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var operatorId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var stopId = Guid.NewGuid();
        var confirmed = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(now.AddMinutes(2)), Guid.NewGuid(), tripId, operatorId,
            null, stopId, null, null, Money.FromRaw(100_000), Money.Zero, Money.FromRaw(100_000),
            tripSnapshotDeparture: now.AddHours(6));
        confirmed.Confirm(now.AddHours(-1));
        var pendingPayment = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(now.AddMinutes(3)), Guid.NewGuid(), tripId, operatorId,
            null, stopId, null, null, Money.FromRaw(100_000), Money.Zero, Money.FromRaw(100_000),
            tripSnapshotDeparture: now.AddHours(6));

        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var seed = seedScope.ServiceProvider.GetRequiredService<BookingDbContext>();
            seed.Bookings.AddRange(confirmed, pendingPayment);
            await seed.SaveChangesAsync();
        }

        var eventId = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            var tripClient = Substitute.For<ITripServiceClient>();
            tripClient.GetTripSnapshotAsync(tripId, Arg.Any<CancellationToken>())
                .Returns(new TripSnapshot(
                    tripId, operatorId, Guid.NewGuid(), Guid.NewGuid(), "SCHEDULED",
                    now.AddHours(6), now.AddHours(7), 100_000,
                    new TripStationSnapshot(Guid.NewGuid(), "Origin"),
                    new TripStationSnapshot(Guid.NewGuid(), "Destination"), [], new TripSeatSummary(10, 10)));
            var handler = new HandleStopDisabledCommandHandler(
                scope.ServiceProvider.GetRequiredService<IBookingRepository>(),
                scope.ServiceProvider.GetRequiredService<IBookingPendingActionRepository>(),
                scope.ServiceProvider.GetRequiredService<IBookingStatsRepository>(),
                new IntegrationEventOutbox(scope.ServiceProvider.GetRequiredService<IOutboxStore>()),
                new EfUnitOfWork(db),
                scope.ServiceProvider.GetRequiredService<VietRide.Shared.Kernel.Abstractions.IClock>(),
                tripClient);

            (await handler.Handle(new HandleStopDisabledCommand(eventId, stopId, operatorId, null), default))
                .Should().Be(1);
        }

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<BookingDbContext>();
        (await verify.BookingPendingActions.AsNoTracking().CountAsync(x => x.BookingId == confirmed.Id))
            .Should().Be(1);
        (await verify.BookingPendingActions.AsNoTracking().CountAsync(x => x.BookingId == pendingPayment.Id))
            .Should().Be(0);
        (await verify.OutboxEvents.AsNoTracking().CountAsync(x => x.Id == eventId))
            .Should().Be(1);
    }
}
