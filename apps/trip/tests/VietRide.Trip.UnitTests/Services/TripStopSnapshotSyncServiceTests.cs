using FluentAssertions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Services;
using VietRide.Trip.Domain.Constants;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Services;

public sealed class TripStopSnapshotSyncServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ActorUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task SynchronizeAsync_DiffsStops_PreservesRetainedFare_AndAuditsActor()
    {
        var routeId = Guid.NewGuid();
        var retainedStopId = Guid.NewGuid();
        var removedStopId = Guid.NewGuid();
        var addedStopId = Guid.NewGuid();
        var trip = CreateTrip(routeId);
        var retained = TripStop.Create(trip.Id, retainedStopId, 1, trip.DepartureDateTime.AddMinutes(10), true, false, 4m);
        var removed = TripStop.Create(trip.Id, removedStopId, 2, trip.DepartureDateTime.AddMinutes(20), true, true, 8m);
        var retainedFare = TripStopFare.Create(trip.Id, retainedStopId, Money.FromRaw(120_000), TripStopFareSource.MANUAL_OVERRIDE);
        var removedFare = TripStopFare.Create(trip.Id, removedStopId, Money.FromRaw(140_000), TripStopFareSource.MANUAL_OVERRIDE);
        var tripStops = new FakeTripStopRepository([retained, removed]);
        var fares = new FakeTripStopFareRepository([retainedFare, removedFare]);
        var audits = new FakeAuditRepository();
        var service = new TripStopSnapshotSyncService(
            new FakeTripRepository([trip]),
            new FakeTripSeatRepository([TripSeat.Create(trip.Id, "A1")]),
            tripStops,
            fares,
            audits,
            new FakeBookingImpactClient());
        var targets = new[]
        {
            RouteStop.Create(routeId, addedStopId, 1, 15, 6m, true, true),
            RouteStop.Create(routeId, retainedStopId, 2, 25, 10m, false, true),
        };

        await service.SynchronizeAsync(
            new TripStopSnapshotSyncPreflight(routeId, OperatorId, [trip.Id]),
            targets,
            ActorUserId,
            "FULL_UPDATE",
            Now,
            CancellationToken.None);

        tripStops.Entities.Select(stop => stop.StopId).Should().BeEquivalentTo([addedStopId, retainedStopId]);
        retained.OrderIndex.Should().Be(2);
        retained.AllowPickup.Should().BeFalse();
        retained.AllowDropoff.Should().BeTrue();
        retained.EstimatedArrivalTime.Should().Be(trip.DepartureDateTime.AddMinutes(25));
        fares.Entities.Should().ContainSingle().Which.Should().BeSameAs(retainedFare);
        audits.Entities.Should().ContainSingle();
        audits.Entities[0].ActorUserId.Should().Be(ActorUserId);
        audits.Entities[0].Action.Should().Be(TripAuditAction.TripStopSnapshotSynced);
    }

    [Fact]
    public async Task SynchronizeAsync_SkipsTrip_WhenASeatIsHeld()
    {
        var routeId = Guid.NewGuid();
        var trip = CreateTrip(routeId);
        var tripStops = new FakeTripStopRepository([]);
        var audits = new FakeAuditRepository();
        var service = new TripStopSnapshotSyncService(
            new FakeTripRepository([trip]),
            new FakeTripSeatRepository([TripSeat.Create(trip.Id, "A1", status: TripSeatStatus.HELD)]),
            tripStops,
            new FakeTripStopFareRepository([]),
            audits,
            new FakeBookingImpactClient());

        await service.SynchronizeAsync(
            new TripStopSnapshotSyncPreflight(routeId, OperatorId, [trip.Id]),
            [RouteStop.Create(routeId, Guid.NewGuid(), 1, 15, 6m, true, true)],
            ActorUserId,
            "ADD_STOP",
            Now,
            CancellationToken.None);

        tripStops.Entities.Should().BeEmpty();
        audits.Entities.Should().BeEmpty();
    }

    private static VietRide.Trip.Domain.Entities.Trip CreateTrip(Guid routeId)
        => VietRide.Trip.Domain.Entities.Trip.Create(
            OperatorId,
            routeId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            Now.AddDays(1),
            Now.AddDays(1).AddHours(3),
            TripSource.MANUAL,
            Money.FromRaw(200_000),
            null,
            0m);

    private sealed class FakeTripRepository(List<VietRide.Trip.Domain.Entities.Trip> entities) : ITripRepository
    {
        public Task<VietRide.Trip.Domain.Entities.Trip> AddAsync(VietRide.Trip.Domain.Entities.Trip entity, CancellationToken ct) { entities.Add(entity); return Task.FromResult(entity); }
        public Task<VietRide.Trip.Domain.Entities.Trip?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(entities.FirstOrDefault(item => item.Id == id));
        public Task<VietRide.Trip.Domain.Entities.Trip?> GetForUpdateAsync(Guid tripId, CancellationToken cancellationToken) => GetByIdAsync(tripId, cancellationToken);
        public IQueryable<VietRide.Trip.Domain.Entities.Trip> Query() => entities.AsQueryable();
        public IQueryable<VietRide.Trip.Domain.Entities.Trip> QueryNoTracking() => entities.AsQueryable();
        public void Remove(VietRide.Trip.Domain.Entities.Trip entity) => entities.Remove(entity);
        public void Update(VietRide.Trip.Domain.Entities.Trip entity) { }
        public Task<VietRide.Trip.Domain.Entities.Trip?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken) => GetByIdAsync(tripId, cancellationToken);
    }

    private sealed class FakeTripSeatRepository(List<TripSeat> entities) : ITripSeatRepository
    {
        public Task<TripSeat> AddAsync(TripSeat entity, CancellationToken ct) { entities.Add(entity); return Task.FromResult(entity); }
        public Task<TripSeat?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(entities.FirstOrDefault(item => item.Id == id));
        public IQueryable<TripSeat> Query() => entities.AsQueryable();
        public IQueryable<TripSeat> QueryNoTracking() => entities.AsQueryable();
        public void Remove(TripSeat entity) => entities.Remove(entity);
        public void Update(TripSeat entity) { }
        public Task<IReadOnlyList<TripSeat>> AcquireForVehicleSwapAsync(Guid tripId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TripSeat>>(entities.Where(item => item.TripId == tripId).ToArray());
    }

    private sealed class FakeTripStopRepository(List<TripStop> entities) : ITripStopRepository
    {
        public List<TripStop> Entities { get; } = entities;
        public Task<TripStop> AddAsync(TripStop entity, CancellationToken ct) { Entities.Add(entity); return Task.FromResult(entity); }
        public Task<TripStop?> GetByIdAsync((Guid TripId, Guid StopId) id, CancellationToken ct) => Task.FromResult(Entities.FirstOrDefault(item => item.TripId == id.TripId && item.StopId == id.StopId));
        public IQueryable<TripStop> Query() => Entities.AsQueryable();
        public IQueryable<TripStop> QueryNoTracking() => Entities.AsQueryable();
        public void Remove(TripStop entity) => Entities.Remove(entity);
        public void RemoveRange(IEnumerable<TripStop> stops) { foreach (var stop in stops.ToArray()) Entities.Remove(stop); }
        public void Update(TripStop entity) { }
        public Task<IReadOnlyList<TripStop>> AcquireByTripAsync(Guid tripId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TripStop>>(Entities.Where(item => item.TripId == tripId).ToArray());
    }

    private sealed class FakeTripStopFareRepository(List<TripStopFare> entities) : ITripStopFareRepository
    {
        public List<TripStopFare> Entities { get; } = entities;
        public Task<TripStopFare> AddAsync(TripStopFare entity, CancellationToken ct) { Entities.Add(entity); return Task.FromResult(entity); }
        public Task<TripStopFare?> GetByIdAsync((Guid TripId, Guid StopId) id, CancellationToken ct) => Task.FromResult(Entities.FirstOrDefault(item => item.TripId == id.TripId && item.StopId == id.StopId));
        public IQueryable<TripStopFare> Query() => Entities.AsQueryable();
        public IQueryable<TripStopFare> QueryNoTracking() => Entities.AsQueryable();
        public void Remove(TripStopFare entity) => Entities.Remove(entity);
        public void RemoveRange(IEnumerable<TripStopFare> fares) { foreach (var fare in fares.ToArray()) Entities.Remove(fare); }
        public void Update(TripStopFare entity) { }
        public Task<IReadOnlyList<TripStopFare>> AcquireByTripAsync(Guid tripId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TripStopFare>>(Entities.Where(item => item.TripId == tripId).ToArray());
    }

    private sealed class FakeAuditRepository : ITripAuditLogRepository
    {
        public List<TripAuditLog> Entities { get; } = [];
        public Task AddAsync(TripAuditLog auditLog, CancellationToken cancellationToken = default) { Entities.Add(auditLog); return Task.CompletedTask; }
        public Task<IReadOnlyList<TripAuditLog>> ListByTripIdAsync(Guid tripId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TripAuditLog>>(Entities.Where(item => item.TripId == tripId).ToArray());
    }

    private sealed class FakeBookingImpactClient : IBookingImpactClient
    {
        public Task<TripBookingImpactProjection> GetTripEditImpactAsync(Guid tripId, Guid operatorId, CancellationToken cancellationToken)
            => Task.FromResult(new TripBookingImpactProjection(tripId, 0, []));
    }
}
