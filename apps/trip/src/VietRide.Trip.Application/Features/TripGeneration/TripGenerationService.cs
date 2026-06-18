using System.Text.Json;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Vehicles;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.TripGeneration;

public sealed class TripGenerationService
{
    private const int GenerationWindowDays = 14;

    private readonly IClock clock;
    private readonly IDriverScheduleRepository driverScheduleRepository;
    private readonly IRouteRepository routeRepository;
    private readonly IRouteStopFareTemplateRepository routeStopFareTemplateRepository;
    private readonly IRouteStopRepository routeStopRepository;
    private readonly ITripGenerationSkipLogRepository skipLogRepository;
    private readonly ITripRepository tripRepository;
    private readonly ITripSeatRepository tripSeatRepository;
    private readonly ITripStopFareRepository tripStopFareRepository;
    private readonly ITripStopRepository tripStopRepository;
    private readonly IVehicleRepository vehicleRepository;

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
        ITripGenerationSkipLogRepository skipLogRepository)
    {
        this.clock = clock;
        this.driverScheduleRepository = driverScheduleRepository;
        this.routeRepository = routeRepository;
        this.routeStopRepository = routeStopRepository;
        this.routeStopFareTemplateRepository = routeStopFareTemplateRepository;
        this.vehicleRepository = vehicleRepository;
        this.tripRepository = tripRepository;
        this.tripSeatRepository = tripSeatRepository;
        this.tripStopRepository = tripStopRepository;
        this.tripStopFareRepository = tripStopFareRepository;
        this.skipLogRepository = skipLogRepository;
    }

    public async Task<GenerateTripsForScheduleResult> GenerateAsync(
        Guid? driverScheduleId,
        CancellationToken cancellationToken)
    {
        var schedules = GetSchedules(driverScheduleId);
        var generatedCount = 0;
        var skippedCount = 0;
        var existingDriverDepartures = PreloadExistingDriverDepartures();
        var existingVehicleDepartures = PreloadExistingVehicleDepartures();

        foreach (var schedule in schedules)
        {
            var route = routeRepository.QueryNoTracking().FirstOrDefault(route => route.Id == schedule.RouteId);
            if (route is null || !route.IsActive || route.DeletedAt is not null)
            {
                skippedCount += await LogSkipAsync(
                    schedule,
                    DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
                    "Route was missing or inactive.",
                    cancellationToken);
                continue;
            }

            var vehicle = schedule.VehicleId.HasValue
                ? vehicleRepository.QueryNoTracking().FirstOrDefault(vehicle => vehicle.Id == schedule.VehicleId.Value)
                : null;
            if (vehicle is null || !vehicle.IsActive || vehicle.DeletedAt is not null)
            {
                skippedCount += await LogSkipAsync(
                    schedule,
                    DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
                    "Vehicle was missing, inactive, or deleted.",
                    cancellationToken);
                continue;
            }

            var routeStops = routeStopRepository.QueryNoTracking()
                .Where(routeStop => routeStop.RouteId == schedule.RouteId)
                .OrderBy(routeStop => routeStop.OrderIndex)
                .ToList();
            var scheduleDays = ParseScheduleDays(schedule.DayOfWeek);
            var serviceDates = MatchingServiceDates(schedule, scheduleDays).ToList();
            var fareTemplates = CurrentFareTemplates(schedule.RouteId).ToList();
            var estimatedTripDurationMinutes = ResolveEstimatedTripDuration(route, routeStops);
            if (!estimatedTripDurationMinutes.HasValue)
            {
                if (driverScheduleId.HasValue)
                {
                    throw MissingEstimatedDurationException();
                }

                skippedCount += await LogMissingDurationSkipsAsync(schedule, serviceDates, cancellationToken);
                continue;
            }

            foreach (var serviceDate in serviceDates)
            {
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

                var trip = Domain.Entities.Trip.Create(
                    schedule.OperatorId,
                    schedule.RouteId,
                    vehicle.Id,
                    schedule.DriverUserId,
                    schedule.AssistantUserId,
                    schedule.Id,
                    departureDateTime,
                    departureDateTime.AddMinutes(estimatedTripDurationMinutes.Value),
                    TripSource.AUTO_FROM_SCHEDULE,
                    route.BaseFare,
                    vehicle.MaxCargoWeightKg,
                    0m);

                await tripRepository.AddAsync(trip, cancellationToken);
                existingDriverDepartures.Add((schedule.DriverUserId, departureDateTime));
                existingVehicleDepartures.Add((vehicle.Id, departureDateTime));
                await AddSeatsAsync(trip.Id, vehicle, cancellationToken);
                await AddStopsAsync(trip.Id, departureDateTime, routeStops, cancellationToken);
                await AddStopFaresAsync(trip.Id, fareTemplates, cancellationToken);
                generatedCount++;
            }
        }

        return new GenerateTripsForScheduleResult(generatedCount, skippedCount);
    }

    private IReadOnlyList<DriverSchedule> GetSchedules(Guid? driverScheduleId)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
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
        CancellationToken cancellationToken)
    {
        var skippedCount = 0;
        IEnumerable<DateOnly> datesToLog = serviceDates.Count == 0
            ? [DateOnly.FromDateTime(clock.UtcNow.UtcDateTime)]
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
        IReadOnlySet<int> scheduleDays)
    {
        var now = clock.UtcNow;
        var localToday = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(7)).DateTime);
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

        foreach (var seat in layout.Seats.Where(seat => !seat.Disabled))
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
        CancellationToken cancellationToken)
    {
        foreach (var routeStop in routeStops)
        {
            await tripStopRepository.AddAsync(
                TripStop.Create(
                    tripId,
                    routeStop.StopId,
                    routeStop.OrderIndex,
                    departureDateTime.AddMinutes(routeStop.EstimatedDurationFromOriginMinutes),
                    routeStop.AllowPickup,
                    routeStop.AllowDropoff,
                    routeStop.DistanceFromOriginKm),
                cancellationToken);
        }
    }

    private async Task AddStopFaresAsync(
        Guid tripId,
        IReadOnlyList<RouteStopFareTemplate> fareTemplates,
        CancellationToken cancellationToken)
    {
        foreach (var fareTemplate in fareTemplates)
        {
            await tripStopFareRepository.AddAsync(
                TripStopFare.Create(tripId, fareTemplate.StopId, fareTemplate.FareFromThisStop),
                cancellationToken);
        }
    }

    private IEnumerable<RouteStopFareTemplate> CurrentFareTemplates(Guid routeId)
    {
        var now = clock.UtcNow;
        return routeStopFareTemplateRepository.QueryNoTracking()
            .Where(template => template.RouteId == routeId
                && template.EffectiveFrom <= now
                && (!template.EffectiveUntil.HasValue || template.EffectiveUntil.Value > now))
            .ToList();
    }

    private static DateTimeOffset BuildDepartureDateTime(DateOnly date, TimeOnly time)
    {
        var localDateTime = date.ToDateTime(time);
        return new DateTimeOffset(localDateTime, TimeSpan.FromHours(7)).ToUniversalTime();
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
        return Enum.TryParse<TripSeatType>(seatType, ignoreCase: true, out var parsed)
            ? parsed
            : TripSeatType.STANDARD;
    }
}
