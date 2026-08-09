using System.Text.Json;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.Vehicles;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.TripGeneration;

public sealed class TripGenerationService
{
    private const int GenerationWindowDays = 14;
    private static readonly TimeSpan IctOffset = TimeSpan.FromHours(7);

    private const string TripAssignedEventType = "trip.trip.assigned";
    private const string SubscriptionLimitTripSkippedEventType = "subscription.limit.trip_skipped";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IClock clock;
    private readonly IDriverScheduleRepository driverScheduleRepository;
    private readonly IRouteRepository routeRepository;
    private readonly IRouteStopRepository routeStopRepository;
    private readonly ITripGenerationSkipLogRepository skipLogRepository;
    private readonly ITripRepository tripRepository;
    private readonly ITripSeatRepository tripSeatRepository;
    private readonly ITripStopRepository tripStopRepository;
    private readonly IVehicleRepository vehicleRepository;
    private readonly IStationRepository? stationRepository;
    private readonly IStopRepository? stopRepository;
    private readonly ITripEtaPlanner? tripEtaPlanner;
    private readonly IIntegrationEventOutbox? outbox;
    private readonly ISubscriptionQuotaClient? quotaClient;
    private readonly List<(Guid OperatorId, Guid AllocationId)> persistedQuotaAllocations = [];

    public TripGenerationService(
        IClock clock,
        IDriverScheduleRepository driverScheduleRepository,
        IRouteRepository routeRepository,
        IRouteStopRepository routeStopRepository,
        IRouteStopFareTemplateRepository routeStopFareTemplateRepository,
        IVehicleRepository vehicleRepository,
        ITripRepository tripRepository,
        ITripSeatRepository tripSeatRepository,
        ITripStopRepository tripStopRepository,
        ITripStopFareRepository tripStopFareRepository,
        ITripGenerationSkipLogRepository skipLogRepository,
        IIntegrationEventOutbox? outbox = null,
        ISubscriptionQuotaClient? quotaClient = null,
        IStationRepository? stationRepository = null,
        IStopRepository? stopRepository = null,
        ITripEtaPlanner? tripEtaPlanner = null)
    {
        this.clock = clock;
        this.driverScheduleRepository = driverScheduleRepository;
        this.routeRepository = routeRepository;
        this.routeStopRepository = routeStopRepository;
        this.vehicleRepository = vehicleRepository;
        this.tripRepository = tripRepository;
        this.tripSeatRepository = tripSeatRepository;
        this.tripStopRepository = tripStopRepository;
        this.skipLogRepository = skipLogRepository;
        this.outbox = outbox;
        this.quotaClient = quotaClient;
        this.stationRepository = stationRepository;
        this.stopRepository = stopRepository;
        this.tripEtaPlanner = tripEtaPlanner;
    }

    public async Task<GenerateTripsForScheduleResult> GenerateAsync(
        Guid? driverScheduleId,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(now.ToOffset(IctOffset).DateTime);
        var schedules = GetSchedules(driverScheduleId, today);
        var generatedCount = 0;
        var skippedCount = 0;
        var existingDriverDepartures = PreloadExistingDriverDepartures();
        var existingVehicleDepartures = PreloadExistingVehicleDepartures();

        foreach (var scheduleCandidate in schedules)
        {
            var schedule = await driverScheduleRepository.AcquireOwnedForUpdateAsync(
                scheduleCandidate.Id,
                scheduleCandidate.OperatorId,
                cancellationToken);
            if (schedule is null
                || !schedule.IsActive
                || (schedule.ValidUntil.HasValue && schedule.ValidUntil.Value < today))
            {
                continue;
            }

            var existingScheduleDates = LoadExistingServiceDates(schedule.Id);
            var route = routeRepository.QueryNoTracking().FirstOrDefault(route => route.Id == schedule.RouteId);
            if (route is null || !route.IsActive || route.DeletedAt is not null)
            {
                skippedCount += await LogSkipAsync(
                    schedule,
                    today,
                    "Route was missing or inactive.",
                    cancellationToken);
                continue;
            }

            var routeStops = routeStopRepository.QueryNoTracking()
                .Where(routeStop => routeStop.RouteId == schedule.RouteId)
                .OrderBy(routeStop => routeStop.OrderIndex)
                .ToList();
            var scheduleDays = ParseScheduleDays(schedule.DayOfWeek);
            var serviceDates = MatchingServiceDates(schedule, scheduleDays, now).ToList();
            var vehicle = schedule.VehicleId.HasValue
                ? vehicleRepository.QueryNoTracking().FirstOrDefault(vehicle => vehicle.Id == schedule.VehicleId.Value)
                : null;
            if (vehicle is null || !vehicle.IsActive || vehicle.DeletedAt is not null)
            {
                var message = schedule.VehicleId.HasValue
                    ? "Assigned vehicle was missing, inactive, or deleted."
                    : "No vehicle is assigned to this DriverSchedule.";
                foreach (var serviceDate in serviceDates)
                {
                    skippedCount += await LogSkipAsync(
                        schedule,
                        serviceDate,
                        TripGenerationSkipReason.OTHER,
                        message,
                        cancellationToken);
                }

                continue;
            }

            var estimatedTripDurationMinutes = ResolveEstimatedTripDuration(route, routeStops);
            if (!estimatedTripDurationMinutes.HasValue)
            {
                if (driverScheduleId.HasValue)
                {
                    throw MissingEstimatedDurationException();
                }

                skippedCount += await LogMissingDurationSkipsAsync(
                    schedule,
                    serviceDates,
                    today,
                    cancellationToken);
                continue;
            }

            foreach (var serviceDate in serviceDates)
            {
                if (existingScheduleDates.Contains(serviceDate))
                {
                    continue;
                }

                var departureDateTime = BuildDepartureDateTime(serviceDate, schedule.DepartureTime);
                if (existingDriverDepartures.Contains((schedule.DriverUserId, departureDateTime))
                    || TripExistsForDriver(schedule.DriverUserId, departureDateTime))
                {
                    skippedCount += await LogSkipAsync(
                        schedule,
                        serviceDate,
                        TripGenerationSkipReason.DRIVER_CONFLICT,
                        "A trip already exists for this driver and departure time.",
                        cancellationToken);
                    continue;
                }

                if (existingVehicleDepartures.Contains((vehicle.Id, departureDateTime))
                    || TripExistsForVehicle(vehicle.Id, departureDateTime))
                {
                    skippedCount += await LogSkipAsync(
                        schedule,
                        serviceDate,
                        TripGenerationSkipReason.VEHICLE_CONFLICT,
                        "A trip already exists for this vehicle and departure time.",
                        cancellationToken);
                    continue;
                }

                var etaPlan = await PlanEtaAsync(
                    route,
                    routeStops,
                    departureDateTime,
                    estimatedTripDurationMinutes.Value,
                    cancellationToken);
                var trip = Domain.Entities.Trip.Create(
                    schedule.OperatorId,
                    schedule.RouteId,
                    vehicle.Id,
                    schedule.DriverUserId,
                    schedule.AssistantUserId,
                    schedule.Id,
                    departureDateTime,
                    etaPlan.DestinationArrivalTime,
                    TripSource.AUTO_FROM_SCHEDULE,
                    route.BaseFare,
                    vehicle.MaxCargoWeightKg,
                    vehicle.MaxCargoVolumeM3,
                    0m,
                    seatLayoutSnapshotJson: vehicle.SeatLayoutJson,
                    plannedEtaSource: etaPlan.Source);

                Guid? quotaAllocationId = null;
                if (quotaClient is not null)
                {
                    var quota = await quotaClient.ClaimQuotaAllocationAsync(
                        schedule.OperatorId,
                        "TRIPS_THIS_MONTH",
                        trip.Id,
                        $"{serviceDate:yyyy-MM}",
                        cancellationToken);
                    if (!quota.IsAllowed)
                    {
                        if (string.Equals(quota.ErrorCode, "SUBSCRIPTION_LIMIT_EXCEEDED", StringComparison.Ordinal))
                        {
                            skippedCount += await LogSubscriptionLimitSkipAsync(
                                schedule,
                                serviceDate,
                                quota.Message ?? "Subscription monthly trip limit exceeded.",
                                cancellationToken);
                            continue;
                        }

                        throw new CodedValidationException(
                            quota.ErrorCode ?? "SUBSCRIPTION_QUOTA_UNAVAILABLE",
                            quota.Message ?? "Unable to allocate subscription trip quota.");
                    }

                    quotaAllocationId = quota.AllocationId;
                }

                try
                {
                    await tripRepository.AddAsync(trip, cancellationToken);
                    existingScheduleDates.Add(serviceDate);
                    existingDriverDepartures.Add((schedule.DriverUserId, departureDateTime));
                    existingVehicleDepartures.Add((vehicle.Id, departureDateTime));
                    await AddSeatsAsync(trip.Id, vehicle, cancellationToken);
                    await AddStopsAsync(trip.Id, departureDateTime, routeStops, etaPlan, cancellationToken);
                    if (outbox is not null)
                    {
                        await outbox.EnqueueAsync(
                            TripAssignedEventType,
                            JsonSerializer.Serialize(new
                            {
                                tripId = trip.Id,
                                operatorId = trip.OperatorId,
                                driverUserId = trip.DriverUserId,
                                assistantUserId = trip.AssistantUserId,
                                routeName = route.Name,
                                vehiclePlateNumber = vehicle.LicensePlate,
                                departureDateTime = trip.DepartureDateTime,
                            }, JsonOptions),
                            cancellationToken);
                    }
                    if (quotaAllocationId.HasValue && quotaAllocationId.Value != Guid.Empty)
                    {
                        persistedQuotaAllocations.Add((schedule.OperatorId, quotaAllocationId.Value));
                    }
                }
                catch
                {
                    await ReleaseQuotaAllocationAsync(schedule.OperatorId, quotaAllocationId, cancellationToken);
                    throw;
                }
                generatedCount++;
            }
        }

        return new GenerateTripsForScheduleResult(generatedCount, skippedCount);
    }

    public async Task ReleasePersistedQuotaAllocationsAsync(CancellationToken cancellationToken)
    {
        foreach (var allocation in persistedQuotaAllocations)
        {
            await ReleaseQuotaAllocationAsync(allocation.OperatorId, allocation.AllocationId, cancellationToken);
        }

        persistedQuotaAllocations.Clear();
    }

    private async Task ReleaseQuotaAllocationAsync(
        Guid operatorId,
        Guid? allocationId,
        CancellationToken cancellationToken)
    {
        if (quotaClient is null || !allocationId.HasValue || allocationId.Value == Guid.Empty)
        {
            return;
        }

        await quotaClient.ReleaseQuotaAllocationAsync(operatorId, allocationId.Value, cancellationToken);
    }

    private IReadOnlyList<DriverSchedule> GetSchedules(Guid? driverScheduleId, DateOnly today)
    {
        var query = driverScheduleRepository.QueryNoTracking()
            .Where(schedule => schedule.IsActive && (!schedule.ValidUntil.HasValue || schedule.ValidUntil.Value >= today));

        if (driverScheduleId.HasValue)
        {
            query = query.Where(schedule => schedule.Id == driverScheduleId.Value);
        }

        return query.ToList();
    }

    private Task<int> LogSkipAsync(
        DriverSchedule schedule,
        DateOnly skippedDate,
        string message,
        CancellationToken cancellationToken)
    {
        return LogSkipAsync(
            schedule,
            skippedDate,
            TripGenerationSkipReason.OTHER,
            message,
            cancellationToken);
    }

    private async Task<int> LogSkipAsync(
        DriverSchedule schedule,
        DateOnly skippedDate,
        TripGenerationSkipReason reason,
        string message,
        CancellationToken cancellationToken)
    {
        await skipLogRepository.AddAsync(
            TripGenerationSkipLog.Create(
                schedule.OperatorId,
                schedule.Id,
                skippedDate,
                reason,
                message),
            cancellationToken);
        return 1;
    }

    private async Task<int> LogSubscriptionLimitSkipAsync(
        DriverSchedule schedule,
        DateOnly skippedDate,
        string message,
        CancellationToken cancellationToken)
    {
        var skippedCount = await LogSkipAsync(
            schedule,
            skippedDate,
            TripGenerationSkipReason.SUBSCRIPTION_LIMIT_EXCEEDED,
            message,
            cancellationToken);
        if (outbox is not null)
        {
            await outbox.EnqueueAsync(
                SubscriptionLimitTripSkippedEventType,
                JsonSerializer.Serialize(new
                {
                    operatorId = schedule.OperatorId,
                    driverScheduleId = schedule.Id,
                    skippedDate,
                    reason = TripGenerationSkipReason.SUBSCRIPTION_LIMIT_EXCEEDED.ToString(),
                    message,
                }, JsonOptions),
                cancellationToken);
        }

        return skippedCount;
    }

    private static int? ResolveEstimatedTripDuration(
        Route route,
        IReadOnlyCollection<RouteStop> routeStops)
    {
        if (route.EstimatedDurationMinutes is > 0)
        {
            return route.EstimatedDurationMinutes.Value;
        }

        var fallback = routeStops.Count == 0
            ? 0
            : routeStops.Max(routeStop => routeStop.EstimatedDurationFromOriginMinutes);
        return fallback > 0 ? fallback : null;
    }

    private async Task<int> LogMissingDurationSkipsAsync(
        DriverSchedule schedule,
        IReadOnlyCollection<DateOnly> serviceDates,
        DateOnly fallbackDate,
        CancellationToken cancellationToken)
    {
        var skippedCount = 0;
        IEnumerable<DateOnly> datesToLog = serviceDates.Count == 0
            ? [fallbackDate]
            : serviceDates;

        foreach (var serviceDate in datesToLog)
        {
            skippedCount += await LogSkipAsync(
                schedule,
                serviceDate,
                "Route duration or route-stop duration is required.",
                cancellationToken);
        }

        return skippedCount;
    }

    private static ValidationException MissingEstimatedDurationException()
    {
        return new ValidationException(
            "Route estimated duration is required for trip generation.",
            [new ValidationError("estimatedArrivalTime", "Route duration or route-stop duration is required.")]);
    }

    private IEnumerable<DateOnly> MatchingServiceDates(
        DriverSchedule schedule,
        IReadOnlySet<int> scheduleDays,
        DateTimeOffset now)
    {
        var localToday = DateOnly.FromDateTime(now.ToOffset(IctOffset).DateTime);
        var generationWindowEnd = now.AddDays(GenerationWindowDays);

        for (var offset = 0; offset <= GenerationWindowDays; offset++)
        {
            var candidate = localToday.AddDays(offset);
            var departureDateTime = BuildDepartureDateTime(candidate, schedule.DepartureTime);
            if (candidate < schedule.ValidFrom
                || (schedule.ValidUntil.HasValue && candidate > schedule.ValidUntil.Value)
                || !scheduleDays.Contains(ToContractDayOfWeek(candidate))
                || departureDateTime <= now
                || departureDateTime > generationWindowEnd)
            {
                continue;
            }

            yield return candidate;
        }
    }

    private HashSet<DateOnly> LoadExistingServiceDates(Guid driverScheduleId)
    {
        return tripRepository.QueryNoTracking()
            .Where(trip => trip.Status != TripStatus.CANCELLED
                && trip.DriverScheduleId == driverScheduleId)
            .AsEnumerable()
            .Select(trip => DateOnly.FromDateTime(trip.DepartureDateTime.ToOffset(IctOffset).DateTime))
            .ToHashSet();
    }

    private HashSet<(Guid DriverUserId, DateTimeOffset DepartureDateTime)> PreloadExistingDriverDepartures()
    {
        return tripRepository.QueryNoTracking()
            .Where(trip => trip.Status != TripStatus.CANCELLED)
            .AsEnumerable()
            .Select(trip => (trip.DriverUserId, trip.DepartureDateTime))
            .ToHashSet();
    }

    private HashSet<(Guid VehicleId, DateTimeOffset DepartureDateTime)> PreloadExistingVehicleDepartures()
    {
        return tripRepository.QueryNoTracking()
            .Where(trip => trip.Status != TripStatus.CANCELLED)
            .AsEnumerable()
            .Select(trip => (trip.VehicleId, trip.DepartureDateTime))
            .ToHashSet();
    }

    private bool TripExistsForDriver(Guid driverUserId, DateTimeOffset departureDateTime)
    {
        return tripRepository.QueryNoTracking().Any(trip =>
            trip.Status != TripStatus.CANCELLED
            && trip.DepartureDateTime == departureDateTime
            && trip.DriverUserId == driverUserId);
    }

    private bool TripExistsForVehicle(Guid vehicleId, DateTimeOffset departureDateTime)
    {
        return tripRepository.QueryNoTracking().Any(trip =>
            trip.Status != TripStatus.CANCELLED
            && trip.DepartureDateTime == departureDateTime
            && trip.VehicleId == vehicleId);
    }

    private async Task AddSeatsAsync(Guid tripId, Vehicle vehicle, CancellationToken cancellationToken)
    {
        var layout = vehicle.SeatLayoutJson.Deserialize<SeatLayoutDto>()
            ?? throw new ValidationException(
                "Vehicle seat layout is required for trip generation.",
                [new ValidationError("seatLayoutJson", "Seat layout could not be parsed.")]);

        foreach (var seat in layout.Seats.Where(SeatLayoutMetrics.IsUsablePassengerSeat))
        {
            await tripSeatRepository.AddAsync(
                TripSeat.Create(tripId, seat.SeatNumber, MapSeatType(seat.Type)),
                cancellationToken);
        }
    }

    private async Task AddStopsAsync(
        Guid tripId,
        DateTimeOffset departureDateTime,
        IReadOnlyList<RouteStop> routeStops,
        TripEtaPlan etaPlan,
        CancellationToken cancellationToken)
    {
        foreach (var routeStop in routeStops)
        {
            await tripStopRepository.AddAsync(
                TripStop.Create(
                    tripId,
                    routeStop.StopId,
                    routeStop.OrderIndex,
                    etaPlan.StopArrivalTimes.GetValueOrDefault(
                        routeStop.StopId,
                        departureDateTime.AddMinutes(routeStop.EstimatedDurationFromOriginMinutes)),
                    routeStop.AllowPickup,
                    routeStop.AllowDropoff,
                    routeStop.DistanceFromOriginKm),
                cancellationToken);
        }
    }

    private async Task<TripEtaPlan> PlanEtaAsync(
        Domain.Entities.Route route,
        IReadOnlyList<RouteStop> routeStops,
        DateTimeOffset departureDateTime,
        int fallbackDurationMinutes,
        CancellationToken cancellationToken)
    {
        var fallback = new TripEtaPlan(
            PlannedEtaSource.ROUTE_BASELINE,
            departureDateTime.AddMinutes(fallbackDurationMinutes),
            routeStops.ToDictionary(
                stop => stop.StopId,
                stop => departureDateTime.AddMinutes(stop.EstimatedDurationFromOriginMinutes)));
        if (tripEtaPlanner is null || stationRepository is null || stopRepository is null)
        {
            return fallback;
        }

        var stationIds = new[] { route.OriginStationId, route.DestinationStationId };
        var stations = stationRepository.QueryNoTracking()
            .Where(station => stationIds.Contains(station.Id))
            .ToDictionary(station => station.Id);
        if (!stations.TryGetValue(route.OriginStationId, out var originStation)
            || !stations.TryGetValue(route.DestinationStationId, out var destinationStation))
        {
            return fallback;
        }

        var stopIds = routeStops.Select(stop => stop.StopId).ToArray();
        var stopById = stopRepository.QueryNoTracking()
            .Where(stop => stopIds.Contains(stop.Id))
            .ToDictionary(stop => stop.Id);
        if (stopById.Count != routeStops.Count)
        {
            return fallback;
        }

        return await tripEtaPlanner.PlanAsync(
            route,
            originStation,
            destinationStation,
            routeStops
                .Select(stop => new TripEtaStopInput(stop, stopById[stop.StopId]))
                .ToArray(),
            departureDateTime,
            cancellationToken);
    }

    private static DateTimeOffset BuildDepartureDateTime(DateOnly date, TimeOnly time)
    {
        var localDateTime = date.ToDateTime(time);
        return new DateTimeOffset(localDateTime, IctOffset).ToUniversalTime();
    }

    private static HashSet<int> ParseScheduleDays(JsonElement dayOfWeek)
    {
        return dayOfWeek.EnumerateArray()
            .Select(day => day.GetInt32())
            .ToHashSet();
    }

    private static int ToContractDayOfWeek(DateOnly date)
    {
        var day = (int)date.DayOfWeek;
        return day == 0 ? 7 : day;
    }

    private static TripSeatType MapSeatType(string? seatType)
    {
        if (!Enum.TryParse<TripSeatType>(seatType, ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed)
            || parsed == TripSeatType.DRIVER_AREA)
        {
            throw new ValidationException(
                "Vehicle seat layout contains an invalid passenger seat type.",
                [new ValidationError("seatLayoutJson.seats[].type", "Seat type must be a ranked passenger type.")]);
        }

        return parsed;
    }
}
