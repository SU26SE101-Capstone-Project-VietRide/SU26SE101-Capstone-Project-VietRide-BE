using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.TripGeneration;
using VietRide.Trip.Application.Features.Vehicles;
using VietRide.Trip.Domain.Entities;
using RouteEntity = VietRide.Trip.Domain.Entities.Route;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Features.DriverSchedules;

public sealed class Day23MutableScheduleGenerationDedupeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MutatedScheduleTime_DoesNotGenerateSecondTripForCoveredIctServiceDate(
        bool cascadeExistingTrip)
    {
        var fixture = Fixture.Create();
        var first = await fixture.Service.GenerateAsync(fixture.Schedule.Id, CancellationToken.None);
        var existingTrip = fixture.Trips.Items.Single();
        var originalDeparture = existingTrip.DepartureDateTime;
        var newDepartureTime = new TimeOnly(21, 0);
        using (var days = JsonDocument.Parse("[3]"))
        {
            fixture.Schedule.UpdateRecurrence(
                newDepartureTime,
                days.RootElement,
                fixture.Schedule.DriverUserId,
                fixture.Schedule.AssistantUserId,
                fixture.Schedule.VehicleId,
                fixture.Schedule.ValidUntil,
                fixture.Schedule.IsActive);
        }

        if (cascadeExistingTrip)
        {
            existingTrip.Reschedule(
                BuildDeparture(fixture.ServiceDate, newDepartureTime),
                BuildDeparture(fixture.ServiceDate, newDepartureTime).AddHours(3));
        }

        var replay = await fixture.Service.GenerateAsync(fixture.Schedule.Id, CancellationToken.None);

        first.GeneratedCount.Should().Be(1);
        replay.GeneratedCount.Should().Be(0);
        replay.SkippedCount.Should().Be(0);
        fixture.Trips.Items.Should().ContainSingle();
        fixture.TripSeats.Items.Should().ContainSingle();
        fixture.SkipLogs.Items.Should().BeEmpty();
        existingTrip.DepartureDateTime.Should().Be(cascadeExistingTrip
            ? BuildDeparture(fixture.ServiceDate, newDepartureTime)
            : originalDeparture);
        fixture.Clock.ReadCount.Should().Be(2, "each generation run captures its clock exactly once");
    }

    private static DateTimeOffset BuildDeparture(DateOnly date, TimeOnly time) =>
        new DateTimeOffset(date.ToDateTime(time), TimeSpan.FromHours(7)).ToUniversalTime();

    private sealed class Fixture
    {
        private Fixture()
        {
            ServiceDate = new DateOnly(2026, 7, 15);
            var operatorId = Guid.NewGuid();
            var route = RouteEntity.Create(
                operatorId,
                "Day 23 generation route",
                Guid.NewGuid(),
                Guid.NewGuid(),
                Money.FromRaw(100_000),
                totalDistanceKm: 100m,
                estimatedDurationMinutes: 180);
            var vehicle = Vehicle.Create(
                operatorId,
                Guid.NewGuid(),
                "51B-DAY23",
                JsonSerializer.SerializeToElement(CreateLayout()),
                totalSeats: 1,
                maxCargoWeightKg: null,
                maxCargoVolumeM3: null);
            Schedule = DriverSchedule.Create(
                operatorId,
                route.Id,
                vehicle.Id,
                Guid.NewGuid(),
                assistantUserId: null,
                JsonSerializer.SerializeToElement(new[] { 3 }),
                new TimeOnly(20, 0),
                ServiceDate,
                ServiceDate,
                isActive: true);
            Clock = new CountingClock(
                new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero),
                maxReads: 2);
            Trips = new TripRepository();
            TripSeats = new SeatRepository();
            SkipLogs = new SkipLogRepository();

            Service = new TripGenerationService(
                Clock,
                new ScheduleRepository(Schedule),
                new RouteRepository(route),
                new RouteStopRepository(),
                Unexpected<IRouteStopFareTemplateRepository>(),
                new VehicleRepository(vehicle),
                Trips,
                TripSeats,
                new StopRepository(),
                Unexpected<ITripStopFareRepository>(),
                SkipLogs);
        }

        public DateOnly ServiceDate { get; }

        public DriverSchedule Schedule { get; }

        public TripGenerationService Service { get; }

        public TripRepository Trips { get; }

        public SeatRepository TripSeats { get; }

        public SkipLogRepository SkipLogs { get; }

        public CountingClock Clock { get; }

        public static Fixture Create() => new();
    }

    private abstract class MemoryRepository<TEntity, TId>
        where TEntity : class
        where TId : notnull
    {
        protected MemoryRepository(List<TEntity>? items = null) => Items = items ?? [];

        public List<TEntity> Items { get; }

        public abstract Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken);

        public Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken)
        {
            Items.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(TEntity entity)
        {
        }

        public void Remove(TEntity entity) => Items.Remove(entity);

        public IQueryable<TEntity> Query() => Items.AsQueryable();

        public IQueryable<TEntity> QueryNoTracking() => Items.AsQueryable();
    }

    private sealed class ScheduleRepository : MemoryRepository<DriverSchedule, Guid>, IDriverScheduleRepository
    {
        public ScheduleRepository(DriverSchedule schedule)
            : base([schedule])
        {
        }

        public override Task<DriverSchedule?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.Id == id));

        public Task<DriverSchedule?> AcquireOwnedForUpdateAsync(
            Guid scheduleId,
            Guid operatorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.SingleOrDefault(item => item.Id == scheduleId && item.OperatorId == operatorId));

        public Task AcquireOverlapLocksAsync(
            Guid driverUserId,
            Guid? assistantUserId,
            Guid? vehicleId,
            IReadOnlyCollection<int> dayOfWeek,
            TimeOnly departureTime,
            DateOnly validFrom,
            DateOnly? validUntil,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> HasDriverConflictAsync(
            Guid driverUserId,
            IReadOnlyCollection<int> dayOfWeek,
            TimeOnly departureTime,
            DateOnly validFrom,
            DateOnly? validUntil,
            Guid? excludeScheduleId = null,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> HasAssistantConflictAsync(
            Guid assistantUserId,
            IReadOnlyCollection<int> dayOfWeek,
            TimeOnly departureTime,
            DateOnly validFrom,
            DateOnly? validUntil,
            Guid? excludeScheduleId = null,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> HasVehicleConflictAsync(
            Guid vehicleId,
            IReadOnlyCollection<int> dayOfWeek,
            TimeOnly departureTime,
            DateOnly validFrom,
            DateOnly? validUntil,
            Guid? excludeScheduleId = null,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class RouteRepository : MemoryRepository<RouteEntity, Guid>, IRouteRepository
    {
        public RouteRepository(RouteEntity route)
            : base([route])
        {
        }

        public override Task<RouteEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.Id == id));

        public Task<RouteEntity?> GetOwnedByIdAsync(
            Guid operatorId,
            Guid routeId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.Id == routeId && item.OperatorId == operatorId));

        public Task<RouteEntity?> GetOwnedActiveByIdAsync(
            Guid operatorId,
            Guid routeId,
            CancellationToken cancellationToken) => GetOwnedByIdAsync(operatorId, routeId, cancellationToken);

        public Task<IReadOnlyList<RouteEntity>> ListByOperatorAsync(
            Guid operatorId,
            string? search,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RouteEntity>>(Items.Where(item => item.OperatorId == operatorId).ToArray());

        public Task<bool> ExistsActiveOwnedByOperatorAsync(
            Guid operatorId,
            Guid routeId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items.Any(item => item.Id == routeId && item.OperatorId == operatorId));
    }

    private sealed class RouteStopRepository : MemoryRepository<RouteStop, (Guid RouteId, Guid StopId)>, IRouteStopRepository
    {
        public override Task<RouteStop?> GetByIdAsync(
            (Guid RouteId, Guid StopId) id,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.RouteId == id.RouteId && item.StopId == id.StopId));

        public Task<bool> ExistsByRouteAndOrderIndexAsync(
            Guid routeId,
            int orderIndex,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items.Any(item => item.RouteId == routeId && item.OrderIndex == orderIndex));

        public Task<RouteStop?> GetByRouteAndStopAsync(
            Guid routeId,
            Guid stopId,
            CancellationToken cancellationToken) => GetByIdAsync((routeId, stopId), cancellationToken);
    }

    private sealed class VehicleRepository : MemoryRepository<Vehicle, Guid>, IVehicleRepository
    {
        public VehicleRepository(Vehicle vehicle)
            : base([vehicle])
        {
        }

        public override Task<Vehicle?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.Id == id));

        public Task<Vehicle?> GetOwnedByIdAsync(
            Guid operatorId,
            Guid vehicleId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.Id == vehicleId && item.OperatorId == operatorId));

        public Task<PagedResult<Vehicle>> ListByOperatorAsync(
            Guid operatorId,
            int page,
            int pageSize,
            string? search,
            string? searchIn,
            string? sortBy,
            string sortDir,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> LicensePlateExistsAsync(
            string licensePlate,
            Guid? excludedVehicleId,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> TryAddAsync(Vehicle vehicle, CancellationToken cancellationToken)
        {
            Items.Add(vehicle);
            return Task.FromResult(true);
        }

        public Task<bool> TryUpdateAsync(Vehicle vehicle, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class TripRepository : MemoryRepository<TripEntity, Guid>, ITripRepository
    {
        public override Task<TripEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.Id == id));

        public Task<TripEntity?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken) =>
            GetByIdAsync(tripId, cancellationToken);
    }

    private sealed class SeatRepository : MemoryRepository<TripSeat, Guid>, ITripSeatRepository
    {
        public override Task<TripSeat?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.Id == id));
    }

    private sealed class StopRepository : MemoryRepository<TripStop, (Guid TripId, Guid StopId)>, ITripStopRepository
    {
        public override Task<TripStop?> GetByIdAsync(
            (Guid TripId, Guid StopId) id,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.TripId == id.TripId && item.StopId == id.StopId));
    }

    private sealed class SkipLogRepository : MemoryRepository<TripGenerationSkipLog, Guid>, ITripGenerationSkipLogRepository
    {
        public override Task<TripGenerationSkipLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.Id == id));
    }

    private sealed class CountingClock(DateTimeOffset now, int maxReads) : IClock
    {
        public int ReadCount { get; private set; }

        public DateTimeOffset UtcNow
        {
            get
            {
                ReadCount++;
                if (ReadCount > maxReads)
                {
                    throw new InvalidOperationException("Trip generation read its clock more than once per run.");
                }

                return now;
            }
        }
    }

    private static SeatLayoutDto CreateLayout() =>
        new(
            1,
            "DAY23",
            1,
            1,
            1,
            1,
            [],
            [new SeatLayoutSeatDto("A01", 1, 1, 1, "STANDARD", true, false, false)]);

    private static T Unexpected<T>()
        where T : class => DispatchProxy.Create<T, UnexpectedDependencyProxy>();

    public class UnexpectedDependencyProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException(
                $"Unexpected dependency call: {targetMethod?.DeclaringType?.Name}.{targetMethod?.Name}");
    }
}
