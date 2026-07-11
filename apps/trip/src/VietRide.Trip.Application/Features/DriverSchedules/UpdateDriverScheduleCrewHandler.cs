using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Stops;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.DriverSchedules;

/// <summary>
/// Changes the recurring schedule crew and propagates the new snapshots only to
/// trips that have not started yet. Each changed trip emits a transactional event.
/// </summary>
public sealed class UpdateDriverScheduleCrewHandler : IRequestHandler<UpdateDriverScheduleCrewCommand, DriverScheduleDto>
{
    private const string CrewChangedEventType = "trip.trip.crew_changed";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDriverScheduleRepository driverScheduleRepository;
    private readonly IIdentityInternalClient identityInternalClient;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IRouteRepository routeRepository;
    private readonly ITripRepository tripRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly IVehicleRepository vehicleRepository;

    public UpdateDriverScheduleCrewHandler(
        IDriverScheduleRepository driverScheduleRepository,
        IIdentityInternalClient identityInternalClient,
        ITripRepository tripRepository,
        IRouteRepository routeRepository,
        IVehicleRepository vehicleRepository,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork)
    {
        this.driverScheduleRepository = driverScheduleRepository;
        this.identityInternalClient = identityInternalClient;
        this.tripRepository = tripRepository;
        this.routeRepository = routeRepository;
        this.vehicleRepository = vehicleRepository;
        this.outbox = outbox;
        this.unitOfWork = unitOfWork;
    }

    public async Task<DriverScheduleDto> Handle(UpdateDriverScheduleCrewCommand request, CancellationToken cancellationToken)
    {
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(identityInternalClient, request.OperatorId, cancellationToken);

        var schedule = await driverScheduleRepository.GetByIdAsync(request.DriverScheduleId, cancellationToken)
            ?? throw new CodedNotFoundException("RESOURCE_NOT_FOUND", "Driver schedule was not found.");
        if (schedule.OperatorId != request.OperatorId)
        {
            throw new CodedNotFoundException("RESOURCE_NOT_FOUND", "Driver schedule was not found.");
        }

        await ValidateAssignedUsersAsync(request.OperatorId, request.DriverUserId, request.AssistantUserId, cancellationToken);

        if (schedule.IsActive && await driverScheduleRepository.HasDriverConflictAsync(
                request.DriverUserId,
                JsonSerializer.Deserialize<int[]>(schedule.DayOfWeek.GetRawText()) ?? [],
                schedule.DepartureTime,
                schedule.ValidFrom,
                schedule.ValidUntil,
                schedule.Id,
                cancellationToken))
        {
            throw new ConflictException("TRIP_DRIVER_CONFLICT", "Driver has a conflicting active schedule.");
        }

        var previousDriverUserId = schedule.DriverUserId;
        var previousAssistantUserId = schedule.AssistantUserId;
        schedule.ChangeCrew(request.DriverUserId, request.AssistantUserId);

        var futureTrips = await tripRepository.Query()
            .Where(trip => trip.DriverScheduleId == schedule.Id
                && (trip.Status == TripStatus.SCHEDULED || trip.Status == TripStatus.BOARDING))
            .ToListAsync(cancellationToken);

        foreach (var trip in futureTrips)
        {
            var oldDriverUserId = trip.DriverUserId;
            var oldAssistantUserId = trip.AssistantUserId;
            trip.ChangeCrew(request.DriverUserId, request.AssistantUserId);

            var routeName = routeRepository.QueryNoTracking()
                .Where(route => route.Id == trip.RouteId)
                .Select(route => route.Name)
                .FirstOrDefault() ?? "Chuyến xe";
            var vehiclePlateNumber = vehicleRepository.QueryNoTracking()
                .Where(vehicle => vehicle.Id == trip.VehicleId)
                .Select(vehicle => vehicle.LicensePlate)
                .FirstOrDefault();

            await outbox.EnqueueAsync(
                CrewChangedEventType,
                JsonSerializer.Serialize(new
                {
                    tripId = trip.Id,
                    operatorId = trip.OperatorId,
                    oldDriverUserId,
                    oldAssistantUserId,
                    driverUserId = trip.DriverUserId,
                    assistantUserId = trip.AssistantUserId,
                    routeName,
                    vehiclePlateNumber,
                    departureDateTime = trip.DepartureDateTime,
                }, JsonOptions),
                cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DriverScheduleMapper.ToDto(schedule);
    }

    private async Task ValidateAssignedUsersAsync(Guid operatorId, Guid driverUserId, Guid? assistantUserId, CancellationToken cancellationToken)
    {
        await ValidateAssignedUserAsync(operatorId, driverUserId, "DRIVER", "driverUserId", cancellationToken);
        if (assistantUserId.HasValue)
        {
            await ValidateAssignedUserAsync(operatorId, assistantUserId.Value, "ASSISTANT", "assistantUserId", cancellationToken);
        }
    }

    private async Task ValidateAssignedUserAsync(Guid operatorId, Guid userId, string expectedRole, string fieldName, CancellationToken cancellationToken)
    {
        var user = await identityInternalClient.GetUserAsync(userId, cancellationToken);
        if (!user.Found || !string.Equals(user.Role, expectedRole, StringComparison.OrdinalIgnoreCase) || user.OperatorId != operatorId)
        {
            var message = !user.Found
                ? user.Message ?? $"Identity user '{userId}' was not found."
                : user.OperatorId != operatorId
                    ? "Identity user must belong to the caller operator."
                    : $"Identity user must have role {expectedRole}.";
            throw new ValidationException(message, [new ValidationError(fieldName, message)]);
        }
    }
}
