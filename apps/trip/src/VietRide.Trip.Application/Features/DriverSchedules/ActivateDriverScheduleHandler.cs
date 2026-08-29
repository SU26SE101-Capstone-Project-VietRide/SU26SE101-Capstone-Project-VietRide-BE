using System.Text.Json;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Jobs;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.ResourceAvailability;
using VietRide.Trip.Application.Features.Stops;
using VietRide.Trip.Application.Features.TripGeneration;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.DriverSchedules;

public sealed class ActivateDriverScheduleHandler : IRequestHandler<ActivateDriverScheduleCommand, DriverScheduleDto>
{
    private readonly IDriverScheduleRepository driverScheduleRepository;
    private readonly IIdentityInternalClient identityInternalClient;
    private readonly IRouteRepository routeRepository;
    private readonly IRouteStopRepository routeStopRepository;
    private readonly IVehicleRepository vehicleRepository;
    private readonly ITripGenerationJobScheduler tripGenerationJobScheduler;
    private readonly IUnitOfWork unitOfWork;
    private readonly IResourceAvailabilityService? resourceAvailability;

    public ActivateDriverScheduleHandler(
        IDriverScheduleRepository driverScheduleRepository,
        IIdentityInternalClient identityInternalClient,
        IRouteRepository routeRepository,
        IRouteStopRepository routeStopRepository,
        IVehicleRepository vehicleRepository,
        ITripGenerationJobScheduler tripGenerationJobScheduler,
        IUnitOfWork unitOfWork,
        IResourceAvailabilityService? resourceAvailability = null)
    {
        this.driverScheduleRepository = driverScheduleRepository;
        this.identityInternalClient = identityInternalClient;
        this.routeRepository = routeRepository;
        this.routeStopRepository = routeStopRepository;
        this.vehicleRepository = vehicleRepository;
        this.tripGenerationJobScheduler = tripGenerationJobScheduler;
        this.resourceAvailability = resourceAvailability;
        this.unitOfWork = unitOfWork;
    }

    public async Task<DriverScheduleDto> Handle(ActivateDriverScheduleCommand request, CancellationToken cancellationToken)
    {
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(
            identityInternalClient,
            request.OperatorId,
            cancellationToken);

        var schedule = await driverScheduleRepository.GetByIdAsync(request.DriverScheduleId, cancellationToken)
            ?? throw new CodedNotFoundException("RESOURCE_NOT_FOUND", "Driver schedule was not found.");

        if (schedule.OperatorId != request.OperatorId)
        {
            throw new CodedNotFoundException("RESOURCE_NOT_FOUND", "Driver schedule was not found.");
        }

        if (schedule.IsActive)
        {
            return DriverScheduleMapper.ToDto(schedule);
        }

        await ValidateAssignedUsersAsync(
            schedule.OperatorId,
            schedule.DriverUserId,
            schedule.AssistantUserId,
            cancellationToken);

        if (schedule.VehicleId.HasValue)
        {
            var vehicle = await vehicleRepository.GetOwnedByIdAsync(
                schedule.OperatorId,
                schedule.VehicleId.Value,
                cancellationToken);
            if (vehicle is null
                || !vehicle.IsActive
                || vehicle.DeletedAt.HasValue
                || vehicle.Status != VehicleStatus.ACTIVE)
            {
                throw new CodedValidationException(
                    "VEHICLE_NOT_ACTIVE",
                    "Assigned vehicle must be active before schedule activation.");
            }
        }

        EnsureRouteCanGenerateTrips(schedule);
        var availabilityInput = new DriverScheduleAvailabilityInput(
            schedule.OperatorId,
            schedule.RouteId,
            schedule.VehicleId,
            schedule.DriverUserId,
            schedule.AssistantUserId,
            JsonSerializer.Deserialize<int[]>(schedule.DayOfWeek.GetRawText()) ?? [],
            schedule.DepartureTime,
            schedule.ValidFrom,
            schedule.ValidUntil,
            schedule.Id,
            ExcludePendingTripsFromSchedule: true);
        if (resourceAvailability is null)
        {
            if (await driverScheduleRepository.HasDriverConflictAsync(
                    schedule.DriverUserId,
                    availabilityInput.DayOfWeek,
                    schedule.DepartureTime,
                    schedule.ValidFrom,
                    schedule.ValidUntil,
                    excludeScheduleId: schedule.Id,
                    cancellationToken: cancellationToken))
            {
                throw new ConflictException("TRIP_DRIVER_CONFLICT", "Driver has a conflicting active schedule.");
            }
        }
        else
        {
            ResourceAvailabilityConflictGuard.EnsureAvailable(
                await resourceAvailability.CheckDriverScheduleAsync(
                    availabilityInput,
                    acquireLocks: true,
                    cancellationToken),
                AssignmentSourceType.DRIVER_SCHEDULE);
        }

        schedule.Activate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        tripGenerationJobScheduler.EnqueueScheduleGeneration(schedule.Id);

        return DriverScheduleMapper.ToDto(schedule);
    }

    private async Task ValidateAssignedUsersAsync(
        Guid operatorId,
        Guid driverUserId,
        Guid? assistantUserId,
        CancellationToken cancellationToken)
    {
        await ValidateAssignedUserAsync(operatorId, driverUserId, "DRIVER", "driverUserId", cancellationToken);

        if (assistantUserId.HasValue)
        {
            await ValidateAssignedUserAsync(operatorId, assistantUserId.Value, "ASSISTANT", "assistantUserId", cancellationToken);
        }
    }

    private async Task ValidateAssignedUserAsync(
        Guid operatorId,
        Guid userId,
        string expectedRole,
        string fieldName,
        CancellationToken cancellationToken)
    {
        var user = await identityInternalClient.GetUserAsync(userId, cancellationToken);
        if (!user.Found)
        {
            throw AssignmentValidationFailure(fieldName, user.Message ?? $"Identity user '{userId}' was not found.");
        }

        if (!string.Equals(user.Role, expectedRole, StringComparison.OrdinalIgnoreCase))
        {
            throw AssignmentValidationFailure(fieldName, $"Identity user must have role {expectedRole}.");
        }

        if (user.OperatorId != operatorId)
        {
            throw AssignmentValidationFailure(fieldName, "Identity user must belong to the caller operator.");
        }

        if (!string.Equals(user.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw AssignmentValidationFailure(fieldName, "Identity user must be active before assignment.");
        }
    }

    private static ValidationException AssignmentValidationFailure(string fieldName, string message)
    {
        return new ValidationException(message, [new ValidationError(fieldName, message)]);
    }

    private void EnsureRouteCanGenerateTrips(DriverSchedule schedule)
    {
        var route = routeRepository.QueryNoTracking().FirstOrDefault(route => route.Id == schedule.RouteId)
            ?? throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Route was not found.");
        if (!route.IsActive || route.DeletedAt is not null)
        {
            throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Route was not found.");
        }

        var routeStops = routeStopRepository.QueryNoTracking()
            .Where(routeStop => routeStop.RouteId == schedule.RouteId)
            .ToList();
        var resolvedDuration = route.EstimatedDurationMinutes is > 0
            ? route.EstimatedDurationMinutes.Value
            : routeStops.Select(routeStop => routeStop.EstimatedDurationFromOriginMinutes).DefaultIfEmpty(0).Max();
        if (resolvedDuration <= 0)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Route estimated duration is required for trip generation.",
                [new ValidationError("estimatedArrivalTime", "Route duration or route-stop duration is required.")]);
        }
    }
}
