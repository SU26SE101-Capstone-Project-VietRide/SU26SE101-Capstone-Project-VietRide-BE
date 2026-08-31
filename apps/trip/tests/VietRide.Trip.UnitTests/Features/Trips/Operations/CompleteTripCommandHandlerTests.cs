using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Trips.Operations;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.UnitTests.TestDoubles;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Features.Trips.Operations;

public sealed class CompleteTripCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_AssignedDriver_CompletesAndPublishesCanonicalEvent()
    {
        var driverId = Guid.NewGuid();
        var trip = CreateInProgressTrip(driverId, Guid.NewGuid());
        trip.MarkDestinationArrived(Now.AddMinutes(-1), driverId);
        var outbox = new FakeOutbox();
        var handler = CreateHandler(trip, outbox);

        var result = await handler.Handle(
            new CompleteTripCommand(trip.Id, driverId),
            CancellationToken.None);

        result.Status.Should().Be("COMPLETED");
        result.CompletedAt.Should().Be(Now);
        trip.CompletedByUserId.Should().Be(driverId);
        outbox.Events.Should().ContainSingle();
        using var payload = JsonDocument.Parse(outbox.Events.Single().Payload);
        payload.RootElement.GetProperty("tripId").GetGuid().Should().Be(trip.Id);
        payload.RootElement.GetProperty("operatorId").GetGuid().Should().Be(trip.OperatorId);
        payload.RootElement.GetProperty("terminalAt").GetDateTimeOffset().Should().Be(Now);
        payload.RootElement.GetProperty("hasSubstitution").GetBoolean().Should().BeFalse();
        payload.RootElement.GetProperty("eventId").GetGuid().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_UnassignedCrew_RejectsAndRollsBack()
    {
        var trip = CreateInProgressTrip(Guid.NewGuid(), Guid.NewGuid());
        var outbox = new FakeOutbox();
        var handler = CreateHandler(trip, outbox);

        var action = () => handler.Handle(
            new CompleteTripCommand(trip.Id, Guid.NewGuid()),
            CancellationToken.None);

        await action.Should().ThrowAsync<ForbiddenException>();
        trip.Status.Should().Be(TripStatus.IN_PROGRESS);
        outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AutomaticFallback_CompletesWithoutHumanActor()
    {
        var trip = CreateInProgressTrip(Guid.NewGuid(), null);
        var handler = CreateHandler(trip, new FakeOutbox());

        await handler.Handle(
            new CompleteTripCommand(trip.Id, null, IsAutomatic: true),
            CancellationToken.None);

        trip.Status.Should().Be(TripStatus.COMPLETED);
        trip.CompletedByUserId.Should().BeNull();
    }

    private static CompleteTripCommandHandler CreateHandler(
        TripEntity trip,
        FakeOutbox outbox)
        => new(
            new FakeTripRepository(trip),
            outbox,
            new FrozenClock(Now),
            NullLogger<CompleteTripCommandHandler>.Instance,
            new ClearParcelImpactClient());

    private static TripEntity CreateInProgressTrip(Guid driverId, Guid? assistantId)
    {
        var trip = TripEntity.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            driverId,
            assistantId,
            null,
            Now.AddHours(-3),
            Now.AddHours(-1),
            TripSource.MANUAL,
            Money.FromRaw(100_000),
            1_000m,
            100m);
        trip.MarkBoarding(Now.AddHours(-3));
        trip.Start(Now.AddHours(-3));
        return trip;
    }

    private sealed class FrozenClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakeTripRepository(TripEntity trip) : ITripRepository
    {
        public Task<TripEntity?> GetForUpdateAsync(Guid tripId, CancellationToken cancellationToken)
            => Task.FromResult<TripEntity?>(trip.Id == tripId ? trip : null);

        public Task<TripEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => GetForUpdateAsync(id, cancellationToken);

        public Task<TripEntity> AddAsync(TripEntity entity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Update(TripEntity entity) => throw new NotSupportedException();
        public void Remove(TripEntity entity) => throw new NotSupportedException();
        public IQueryable<TripEntity> Query() => new[] { trip }.AsQueryable();
        public IQueryable<TripEntity> QueryNoTracking() => Query();
        public Task<TripEntity?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken)
            => GetForUpdateAsync(tripId, cancellationToken);
    }

    private sealed class FakeOutbox : IIntegrationEventOutbox
    {
        public List<(string EventType, string Payload)> Events { get; } = [];

        public Task EnqueueAsync(string eventType, string payloadJson, CancellationToken ct = default)
        {
            Events.Add((eventType, payloadJson));
            return Task.CompletedTask;
        }
    }

}
