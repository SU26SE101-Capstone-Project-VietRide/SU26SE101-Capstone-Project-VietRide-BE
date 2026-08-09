using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Hangfire;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.TripGeneration;
using VietRide.Trip.Application.Features.Vehicles;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure.Jobs;
using Route = VietRide.Trip.Domain.Entities.Route;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Features.TripGeneration;

public sealed class TripGenerationServiceTests
{
    [Fact]
    public async Task GenerateAsync_MatchingScheduleDays_CreatesTripsSeatsAndStopSnapshots()
    {
        var fixture = TripGenerationFixture.Create(routeDurationMinutes: 180);

        var result = await fixture.Service.GenerateAsync(fixture.Schedule.Id, CancellationToken.None);

        result.GeneratedCount.Should().Be(4);
        result.SkippedCount.Should().Be(0);
        fixture.Trips.Items.Select(trip => ToContractDayOfWeek(DateOnly.FromDateTime(trip.DepartureDateTime.Date)))
            .Should().OnlyContain(day => day == 2 || day == 4);
        fixture.TripSeats.Items.Should().HaveCount(8);
        fixture.TripSeats.Items.Select(seat => seat.SeatNumber).Should().OnlyContain(seat => seat == "A1" || seat == "A3");
        fixture.TripStops.Items.Should().HaveCount(8);
        fixture.TripStopFares.Items.Should().BeEmpty();

        var firstTrip = fixture.Trips.Items.OrderBy(trip => trip.DepartureDateTime).First();
        firstTrip.Source.Should().Be(TripSource.AUTO_FROM_SCHEDULE);
        firstTrip.DepartureDateTime.Offset.Should().Be(TimeSpan.Zero);
        firstTrip.DepartureDateTime.Should().Be(BuildUtcDepartureDateTime(new DateOnly(2026, 6, 16), fixture.Schedule.DepartureTime));
        firstTrip.EstimatedArrivalTime.Should().Be(firstTrip.DepartureDateTime.AddMinutes(180));
        firstTrip.BaseFare.Amount.Should().Be(250_000);

        var firstTripStops = fixture.TripStops.Items
            .Where(stop => stop.TripId == firstTrip.Id)
            .OrderBy(stop => stop.OrderIndex)
            .ToList();
        firstTripStops[0].OrderIndex.Should().Be(1);
        firstTripStops[0].AllowPickup.Should().BeTrue();
        firstTripStops[0].AllowDropoff.Should().BeFalse();
        firstTripStops[0].DistanceFromOriginKm.Should().Be(0m);
        firstTripStops[0].EstimatedArrivalTime.Should().Be(firstTrip.DepartureDateTime);
        firstTripStops[1].OrderIndex.Should().Be(2);
        firstTripStops[1].AllowPickup.Should().BeFalse();
        firstTripStops[1].AllowDropoff.Should().BeTrue();
        firstTripStops[1].DistanceFromOriginKm.Should().Be(120.5m);
        firstTripStops[1].EstimatedArrivalTime.Should().Be(firstTrip.DepartureDateTime.AddMinutes(210));
    }

    [Fact]
    public async Task GenerateAsync_ScheduleBaseFareOverride_SnapshotsOverrideIntoTrips()
    {
        var fixture = TripGenerationFixture.Create(
            routeDurationMinutes: 180,
            scheduleBaseFare: 400_000);

        await fixture.Service.GenerateAsync(fixture.Schedule.Id, CancellationToken.None);

        fixture.Trips.Items.Should().NotBeEmpty();
        fixture.Trips.Items.Should().OnlyContain(trip => trip.BaseFare.Amount == 400_000);
    }

    [Fact]
    public async Task GenerateAsync_AcquiresScheduleLockBeforeCreatingAnyTrip()
    {
        var fixture = TripGenerationFixture.Create(routeDurationMinutes: 180);
        fixture.Schedules.OnAcquire = () => fixture.Trips.Items.Should().BeEmpty();

        await fixture.Service.GenerateAsync(fixture.Schedule.Id, CancellationToken.None);

        fixture.Schedules.Calls.Should().Equal("schedule-lock");
        fixture.Trips.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GenerateAsync_ReRunSameWindow_DoesNotCreateDuplicateTrips()
    {
        var fixture = TripGenerationFixture.Create(routeDurationMinutes: 180);

        await fixture.Service.GenerateAsync(fixture.Schedule.Id, CancellationToken.None);
        var countAfterFirstRun = fixture.Trips.Items.Count;
        var result = await fixture.Service.GenerateAsync(fixture.Schedule.Id, CancellationToken.None);

        result.GeneratedCount.Should().Be(0);
        fixture.Trips.Items.Should().HaveCount(countAfterFirstRun);
    }

    [Fact]
    public async Task GenerateAsync_SundayScheduleAfterDepartureTime_DoesNotCreatePastTrip()
    {
        var fixture = TripGenerationFixture.Create(
            routeDurationMinutes: 180,
            utcNow: new DateTimeOffset(2026, 6, 14, 16, 0, 0, TimeSpan.Zero),
            dayOfWeek: [2, 7],
            departureTime: new TimeOnly(8, 0),
            validFrom: new DateOnly(2026, 6, 14),
            validUntil: new DateOnly(2026, 7, 31));

        var result = await fixture.Service.GenerateAsync(fixture.Schedule.Id, CancellationToken.None);

        result.GeneratedCount.Should().BeGreaterThan(0);
        fixture.Trips.Items.Should().NotContain(trip => trip.DepartureDateTime == BuildUtcDepartureDateTime(new DateOnly(2026, 6, 14), new TimeOnly(8, 0)));
        fixture.Trips.Items.Should().Contain(trip => trip.DepartureDateTime == BuildUtcDepartureDateTime(new DateOnly(2026, 6, 16), new TimeOnly(8, 0)));
        fixture.Trips.Items.Should().OnlyContain(trip => trip.DepartureDateTime.Offset == TimeSpan.Zero);
        fixture.Trips.Items.Should().OnlyContain(trip => trip.DepartureDateTime > new DateTimeOffset(2026, 6, 14, 16, 0, 0, TimeSpan.Zero));
        fixture.Trips.Items.Should().OnlyContain(trip => trip.DepartureDateTime <= new DateTimeOffset(2026, 6, 28, 16, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task GenerateAsync_UsesRouteStopDurationFallback_WhenRouteDurationIsMissing()
    {
        var fixture = TripGenerationFixture.Create(routeDurationMinutes: null);

        await fixture.Service.GenerateAsync(fixture.Schedule.Id, CancellationToken.None);

        var firstTrip = fixture.Trips.Items.OrderBy(trip => trip.DepartureDateTime).First();
        firstTrip.EstimatedArrivalTime.Should().Be(firstTrip.DepartureDateTime.AddMinutes(210));
    }

    [Fact]
    public async Task GenerateAsync_AllSchedules_MissingDuration_LogsSkipRowsAndContinues()
    {
        var fixture = TripGenerationFixture.Create(routeDurationMinutes: null, includeRouteStops: false);

        var result = await fixture.Service.GenerateAsync(null, CancellationToken.None);

        result.GeneratedCount.Should().Be(0);
        result.SkippedCount.Should().Be(4);
        fixture.SkipLogs.Items.Should().HaveCount(4);
        fixture.SkipLogs.Items.Should().OnlyContain(log => log.Message == "Route duration or route-stop duration is required.");
        fixture.Trips.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateAsync_MissingDurationAndRouteStops_ThrowsValidationException()
    {
        var fixture = TripGenerationFixture.Create(routeDurationMinutes: null, includeRouteStops: false);

        var action = () => fixture.Service.GenerateAsync(fixture.Schedule.Id, CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().Contain(error => error.Field == "estimatedArrivalTime");
        fixture.Trips.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateAsync_MissingVehicle_LogsSkipAndDoesNotCreateTrip()
    {
        var fixture = TripGenerationFixture.Create(routeDurationMinutes: 180);
        fixture.Schedule.AssignVehicle(null);

        var result = await fixture.Service.GenerateAsync(fixture.Schedule.Id, CancellationToken.None);

        result.GeneratedCount.Should().Be(0);
        result.SkippedCount.Should().Be(4);
        fixture.SkipLogs.Items.Should().HaveCount(4)
            .And.OnlyContain(log =>
                log.DriverScheduleId == fixture.Schedule.Id
                && log.Reason == TripGenerationSkipReason.OTHER
                && log.Message!.Contains("No vehicle", StringComparison.Ordinal));
        fixture.Trips.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateAsync_TwoSchedulesSameVehicleAndDepartureInSingleRun_AddsOneTripAndLogsVehicleConflict()
    {
        var fixture = TripGenerationFixture.Create(
            routeDurationMinutes: 180,
            dayOfWeek: [2],
            validUntil: new DateOnly(2026, 6, 16));
        var conflictingSchedule = DriverSchedule.Create(
            fixture.Schedule.OperatorId,
            fixture.Schedule.RouteId,
            fixture.Schedule.VehicleId,
            Guid.NewGuid(),
            null,
            fixture.Schedule.DayOfWeek,
            fixture.Schedule.DepartureTime,
            fixture.Schedule.ValidFrom,
            fixture.Schedule.ValidUntil,
            isActive: true);
        fixture.Schedules.Items.Add(conflictingSchedule);

        var result = await fixture.Service.GenerateAsync(null, CancellationToken.None);

        result.GeneratedCount.Should().Be(1);
        result.SkippedCount.Should().Be(1);
        fixture.Trips.Items.Should().ContainSingle();
        fixture.SkipLogs.Items.Should().ContainSingle(log =>
            log.DriverScheduleId == conflictingSchedule.Id
            && log.Reason == TripGenerationSkipReason.VEHICLE_CONFLICT);
    }

    [Fact]
    public async Task GenerateAsync_VehicleConflict_LogsVehicleConflictSkipAndContinuesOtherDates()
    {
        var fixture = TripGenerationFixture.Create(routeDurationMinutes: 180);
        var departureDateTime = BuildUtcDepartureDateTime(new DateOnly(2026, 6, 16), fixture.Schedule.DepartureTime);
        fixture.Trips.Items.Add(
            TripEntity.Create(
                fixture.Schedule.OperatorId,
                fixture.Schedule.RouteId,
                fixture.Schedule.VehicleId!.Value,
                Guid.NewGuid(),
                null,
                driverScheduleId: null,
                departureDateTime,
                departureDateTime.AddMinutes(180),
                TripSource.AUTO_FROM_SCHEDULE,
                Money.FromRaw(250000),
                1000m,
                0m));

        var result = await fixture.Service.GenerateAsync(fixture.Schedule.Id, CancellationToken.None);

        result.GeneratedCount.Should().Be(3);
        result.SkippedCount.Should().Be(1);
        fixture.SkipLogs.Items.Should().ContainSingle(log => log.Reason == TripGenerationSkipReason.VEHICLE_CONFLICT);
        fixture.Trips.Items.Should().HaveCount(4);
    }

    [Fact]
    public async Task GenerateAsync_SubscriptionLimit_LogsScheduleSkipWithoutPersistingTripSnapshots()
    {
        var quotaClient = new RejectingQuotaClient();
        var fixture = TripGenerationFixture.Create(routeDurationMinutes: 180, quotaClient: quotaClient);

        var result = await fixture.Service.GenerateAsync(fixture.Schedule.Id, CancellationToken.None);

        result.GeneratedCount.Should().Be(0);
        result.SkippedCount.Should().Be(4);
        fixture.Trips.Items.Should().BeEmpty();
        fixture.TripSeats.Items.Should().BeEmpty();
        fixture.TripStops.Items.Should().BeEmpty();
        fixture.TripStopFares.Items.Should().BeEmpty();
        fixture.SkipLogs.Items.Should().OnlyContain(log =>
            log.DriverScheduleId == fixture.Schedule.Id
            && log.Reason == TripGenerationSkipReason.SUBSCRIPTION_LIMIT_EXCEEDED);
        quotaClient.Claims.Should().HaveCount(4);
        quotaClient.Claims.Should().OnlyContain(claim => claim.Resource == "TRIPS_THIS_MONTH" && claim.PeriodKey == "2026-06");
    }

    [Fact]
    public void TripGenerationJobMethods_RunOnTripQueue()
    {
        var scheduleMethod = typeof(TripGenerationJob)
            .GetMethod(nameof(TripGenerationJob.GenerateForScheduleAsync))!;
        scheduleMethod.GetCustomAttribute<QueueAttribute>()!.Queue.Should().Be("trip");
        scheduleMethod.GetCustomAttribute<DisableConcurrentExecutionAttribute>().Should().NotBeNull();

        var activeSchedulesMethod = typeof(TripGenerationJob)
            .GetMethod(nameof(TripGenerationJob.GenerateForActiveSchedulesAsync))!;
        activeSchedulesMethod.GetCustomAttribute<QueueAttribute>()!.Queue.Should().Be("trip");
        activeSchedulesMethod.GetCustomAttribute<DisableConcurrentExecutionAttribute>().Should().NotBeNull();
    }

    private static int ToContractDayOfWeek(DateOnly date)
    {
        var day = (int)date.DayOfWeek;
        return day == 0 ? 7 : day;
    }

    private static DateTimeOffset BuildUtcDepartureDateTime(DateOnly date, TimeOnly time)
    {
        var localDateTime = date.ToDateTime(time);
        return new DateTimeOffset(localDateTime, TimeSpan.FromHours(7)).ToUniversalTime();
    }

    private sealed class TripGenerationFixture
    {
        private TripGenerationFixture(
            TripGenerationService service,
            DriverSchedule schedule,
            InMemoryDriverScheduleRepository schedules,
            InMemoryTripRepository trips,
            InMemoryTripSeatRepository tripSeats,
            InMemoryTripStopRepository tripStops,
            InMemoryTripStopFareRepository tripStopFares,
            InMemorySkipLogRepository skipLogs)
        {
            Service = service;
            Schedule = schedule;
            Schedules = schedules;
            Trips = trips;
            TripSeats = tripSeats;
            TripStops = tripStops;
            TripStopFares = tripStopFares;
            SkipLogs = skipLogs;
        }

        public TripGenerationService Service { get; }

        public DriverSchedule Schedule { get; }

        public InMemoryDriverScheduleRepository Schedules { get; }

        public InMemoryTripRepository Trips { get; }

        public InMemoryTripSeatRepository TripSeats { get; }

        public InMemoryTripStopRepository TripStops { get; }

        public InMemoryTripStopFareRepository TripStopFares { get; }

        public InMemorySkipLogRepository SkipLogs { get; }

        public static TripGenerationFixture Create(
            int? routeDurationMinutes,
            bool includeVehicle = true,
            bool includeRouteStops = true,
            DateTimeOffset? utcNow = null,
            IReadOnlyCollection<int>? dayOfWeek = null,
            TimeOnly? departureTime = null,
            DateOnly? validFrom = null,
            DateOnly? validUntil = null,
            ISubscriptionQuotaClient? quotaClient = null,
            long? scheduleBaseFare = null)
        {
            var operatorId = Guid.NewGuid();
            var route = Route.Create(
                operatorId,
                "Saigon to Can Tho",
                Guid.NewGuid(),
                Guid.NewGuid(),
                Money.FromRaw(250000),
                120.5m,
                routeDurationMinutes);
            var vehicle = Vehicle.Create(
                operatorId,
                Guid.NewGuid(),
                "51B-12345",
                JsonSerializer.SerializeToElement(CreateSeatLayout()),
                4,
                1000m,
                null);
            var schedule = DriverSchedule.Create(
                operatorId,
                route.Id,
                vehicle.Id,
                Guid.NewGuid(),
                Guid.NewGuid(),
                JsonSerializer.SerializeToElement((dayOfWeek ?? [2, 4]).ToArray()),
                departureTime ?? new TimeOnly(8, 0),
                validFrom ?? new DateOnly(2026, 6, 15),
                validUntil ?? new DateOnly(2026, 7, 31),
                isActive: true,
                scheduleBaseFare.HasValue ? Money.FromRaw(scheduleBaseFare.Value) : null);

            var schedules = new InMemoryDriverScheduleRepository([schedule]);
            var routes = new InMemoryRouteRepository([route]);
            var routeStops = new InMemoryRouteStopRepository(includeRouteStops
                ? [
                    RouteStop.Create(route.Id, Guid.NewGuid(), 1, 0, 0m, allowPickup: true, allowDropoff: false),
                    RouteStop.Create(route.Id, Guid.NewGuid(), 2, 210, 120.5m, allowPickup: false, allowDropoff: true),
                ]
                : []);
            var currentFareTemplate = RouteStopFareTemplate.Create(
                route.Id,
                routeStops.Items.LastOrDefault()?.StopId ?? Guid.NewGuid(),
                Money.FromRaw(180000),
                new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                null);
            var expiredFareTemplate = RouteStopFareTemplate.Create(
                route.Id,
                routeStops.Items.FirstOrDefault()?.StopId ?? Guid.NewGuid(),
                Money.FromRaw(90000),
                new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
            var futureFareTemplate = RouteStopFareTemplate.Create(
                route.Id,
                routeStops.Items.FirstOrDefault()?.StopId ?? Guid.NewGuid(),
                Money.FromRaw(190000),
                new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                null);
            var routeStopFareTemplates = new InMemoryRouteStopFareTemplateRepository([
                currentFareTemplate,
                expiredFareTemplate,
                futureFareTemplate,
            ]);
            var vehicles = new InMemoryVehicleRepository(includeVehicle ? [vehicle] : []);
            var trips = new InMemoryTripRepository([]);
            var tripSeats = new InMemoryTripSeatRepository([]);
            var tripStops = new InMemoryTripStopRepository([]);
            var tripStopFares = new InMemoryTripStopFareRepository([]);
            var skipLogs = new InMemorySkipLogRepository([]);

            var service = new TripGenerationService(
                new FixedClock(utcNow ?? new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero)),
                schedules,
                routes,
                routeStops,
                routeStopFareTemplates,
                vehicles,
                trips,
                tripSeats,
                tripStops,
                tripStopFares,
                skipLogs,
                quotaClient: quotaClient);

            return new TripGenerationFixture(service, schedule, schedules, trips, tripSeats, tripStops, tripStopFares, skipLogs);
        }

        private static SeatLayoutDto CreateSeatLayout()
            => new(
                1,
                "BUS_3",
                4,
                1,
                4,
                1,
                [],
                [
                    new SeatLayoutSeatDto("A1", 1, 1, 1, "STANDARD", true, false, false),
                    new SeatLayoutSeatDto("A2", 1, 2, 1, "STANDARD", false, true, true),
                    new SeatLayoutSeatDto("A3", 1, 3, 1, "VIP", true, false, false),
                    new SeatLayoutSeatDto("D1", 1, 4, 1, "DRIVER_AREA", false, false, false),
                ]);
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private abstract class InMemoryRepository<TEntity, TId>
        where TEntity : class
        where TId : notnull
    {
        private readonly Func<TEntity, TId> getId;

        protected InMemoryRepository(List<TEntity> items, Func<TEntity, TId> getId)
        {
            Items = items;
            this.getId = getId;
        }

        public List<TEntity> Items { get; }

        public Task<TEntity?> GetByIdAsync(TId id, CancellationToken ct)
            => Task.FromResult(Items.FirstOrDefault(item => EqualityComparer<TId>.Default.Equals(getId(item), id)));

        public Task<TEntity> AddAsync(TEntity entity, CancellationToken ct)
        {
            Items.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(TEntity entity)
        {
        }

        public void Remove(TEntity entity)
        {
            Items.Remove(entity);
        }

        public IQueryable<TEntity> Query() => Items.AsQueryable();

        public IQueryable<TEntity> QueryNoTracking() => Items.AsQueryable();
    }

    private sealed class InMemoryDriverScheduleRepository : InMemoryRepository<DriverSchedule, Guid>, IDriverScheduleRepository
    {
        public InMemoryDriverScheduleRepository(List<DriverSchedule> items)
            : base(items, schedule => schedule.Id)
        {
        }

        public List<string> Calls { get; } = [];

        public Action? OnAcquire { get; set; }

        public Task<DriverSchedule?> AcquireOwnedForUpdateAsync(
            Guid scheduleId,
            Guid operatorId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("schedule-lock");
            OnAcquire?.Invoke();
            return Task.FromResult(Items.FirstOrDefault(schedule =>
                schedule.Id == scheduleId && schedule.OperatorId == operatorId));
        }

        public Task<bool> HasDriverConflictAsync(
            Guid driverUserId,
            IReadOnlyCollection<int> dayOfWeek,
            TimeOnly departureTime,
            DateOnly validFrom,
            DateOnly? validUntil,
            Guid? excludeScheduleId = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }
    }

    private sealed class InMemoryRouteRepository : InMemoryRepository<Route, Guid>, IRouteRepository
    {
        public InMemoryRouteRepository(List<Route> items)
            : base(items, route => route.Id)
        {
        }

        public Task<Route?> GetOwnedByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken)
            => Task.FromResult(Items.FirstOrDefault(route => route.OperatorId == operatorId && route.Id == routeId));

        public Task<Route?> GetOwnedActiveByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken)
            => Task.FromResult(Items.FirstOrDefault(route => route.OperatorId == operatorId && route.Id == routeId && route.IsActive));

        public Task<IReadOnlyList<Route>> ListByOperatorAsync(Guid operatorId, string? search, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Route>>(Items.Where(route => route.OperatorId == operatorId).ToList());

        public Task<bool> ExistsActiveOwnedByOperatorAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken)
            => Task.FromResult(Items.Any(route => route.OperatorId == operatorId && route.Id == routeId && route.IsActive));
    }

    private sealed class InMemoryRouteStopRepository : InMemoryRepository<RouteStop, (Guid RouteId, Guid StopId)>, IRouteStopRepository
    {
        public InMemoryRouteStopRepository(List<RouteStop> items)
            : base(items, routeStop => (routeStop.RouteId, routeStop.StopId))
        {
        }

        public Task<bool> ExistsByRouteAndOrderIndexAsync(Guid routeId, int orderIndex, CancellationToken cancellationToken)
            => Task.FromResult(Items.Any(routeStop => routeStop.RouteId == routeId && routeStop.OrderIndex == orderIndex));

        public Task<RouteStop?> GetByRouteAndStopAsync(Guid routeId, Guid stopId, CancellationToken cancellationToken)
            => Task.FromResult(Items.FirstOrDefault(routeStop => routeStop.RouteId == routeId && routeStop.StopId == stopId));
    }

    private sealed class InMemoryRouteStopFareTemplateRepository : InMemoryRepository<RouteStopFareTemplate, Guid>, IRouteStopFareTemplateRepository
    {
        public InMemoryRouteStopFareTemplateRepository(List<RouteStopFareTemplate> items)
            : base(items, template => template.Id)
        {
        }

        public Task<bool> ExistsOverlappingAsync(
            Guid routeId,
            Guid stopId,
            DateTimeOffset effectiveFrom,
            DateTimeOffset? effectiveUntil,
            CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<IReadOnlyList<RouteStopFareTemplate>> ListByRouteAsync(Guid routeId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RouteStopFareTemplate>>(Items.Where(template => template.RouteId == routeId).ToList());
    }

    private sealed class InMemoryVehicleRepository : InMemoryRepository<Vehicle, Guid>, IVehicleRepository
    {
        public InMemoryVehicleRepository(List<Vehicle> items)
            : base(items, vehicle => vehicle.Id)
        {
        }

        public Task<Vehicle?> GetOwnedByIdAsync(Guid operatorId, Guid vehicleId, CancellationToken cancellationToken)
            => Task.FromResult(Items.FirstOrDefault(vehicle => vehicle.OperatorId == operatorId && vehicle.Id == vehicleId));

        public Task<PagedResult<Vehicle>> ListByOperatorAsync(
            Guid operatorId,
            int page,
            int pageSize,
            string? search,
            string? searchIn,
            string? sortBy,
            string sortDir,
            CancellationToken cancellationToken)
            => Task.FromResult(PagedResult<Vehicle>.Create(Items, page, pageSize, Items.Count));

        public Task<bool> LicensePlateExistsAsync(string licensePlate, Guid? excludedVehicleId, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<bool> TryAddAsync(Vehicle vehicle, CancellationToken cancellationToken)
        {
            Items.Add(vehicle);
            return Task.FromResult(true);
        }

        public Task<bool> TryUpdateAsync(Vehicle vehicle, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private sealed class InMemoryTripRepository : InMemoryRepository<TripEntity, Guid>, ITripRepository
    {
        public InMemoryTripRepository(List<TripEntity> items)
            : base(items, trip => trip.Id)
        {
        }

        public Task<TripEntity?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken)
            => GetByIdAsync(tripId, cancellationToken);
    }

    private sealed class InMemoryTripSeatRepository : InMemoryRepository<TripSeat, Guid>, ITripSeatRepository
    {
        public InMemoryTripSeatRepository(List<TripSeat> items)
            : base(items, seat => seat.Id)
        {
        }
    }

    private sealed class InMemoryTripStopRepository : InMemoryRepository<TripStop, (Guid TripId, Guid StopId)>, ITripStopRepository
    {
        public InMemoryTripStopRepository(List<TripStop> items)
            : base(items, stop => (stop.TripId, stop.StopId))
        {
        }
    }

    private sealed class InMemoryTripStopFareRepository : InMemoryRepository<TripStopFare, (Guid TripId, Guid StopId)>, ITripStopFareRepository
    {
        public InMemoryTripStopFareRepository(List<TripStopFare> items)
            : base(items, fare => (fare.TripId, fare.StopId))
        {
        }
    }

    private sealed class InMemorySkipLogRepository : InMemoryRepository<TripGenerationSkipLog, Guid>, ITripGenerationSkipLogRepository
    {
        public InMemorySkipLogRepository(List<TripGenerationSkipLog> items)
            : base(items, log => log.Id)
        {
        }
    }

    private sealed class RejectingQuotaClient : ISubscriptionQuotaClient
    {
        public List<(Guid OperatorId, string Resource, Guid ResourceId, string? PeriodKey)> Claims { get; } = [];

        public Task<QuotaAllocationResult> ClaimQuotaAllocationAsync(
            Guid operatorId,
            string resource,
            Guid resourceId,
            string? periodKey,
            CancellationToken cancellationToken = default)
        {
            Claims.Add((operatorId, resource, resourceId, periodKey));
            return Task.FromResult(QuotaAllocationResult.Rejected(
                422,
                "SUBSCRIPTION_LIMIT_EXCEEDED",
                "Subscription monthly trip limit exceeded."));
        }

        public Task ReleaseQuotaAllocationAsync(Guid operatorId, Guid allocationId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
