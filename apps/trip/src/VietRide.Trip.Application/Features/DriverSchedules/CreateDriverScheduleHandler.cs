using System.Text.Json;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Jobs;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.ResourceAvailability;
using VietRide.Trip.Application.Features.Routes;
using VietRide.Trip.Application.Features.Stops;
using VietRide.Trip.Application.Features.TripGeneration;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.DriverSchedules;

public sealed class CreateDriverScheduleHandler : IRequestHandler<CreateDriverScheduleCommand, DriverScheduleDto>
{
    private readonly IDriverScheduleRepository driverScheduleRepository;
    private readonly IIdentityInternalClient identityInternalClient;
    private readonly IRouteRepository routeRepository;
    private readonly IRouteStopRepository routeStopRepository;
    private readonly ITripGenerationJobScheduler tripGenerationJobScheduler;
    private readonly IUnitOfWork unitOfWork;
    private readonly IVehicleRepository vehicleRepository;
    private readonly IResourceAvailabilityService? resourceAvailability;

    public CreateDriverScheduleHandler(
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

    public async Task<DriverScheduleDto> Handle(
        CreateDriverScheduleCommand request,
        CancellationToken cancellationToken)
    {
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(
            identityInternalClient,
            request.OperatorId,
            cancellationToken);

        if (!await routeRepository.ExistsActiveOwnedByOperatorAsync(
                request.OperatorId,
                request.RouteId,
                cancellationToken))
        {
            throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Route was not found.");
        }

        if (request.VehicleId.HasValue
            && await vehicleRepository.GetOwnedByIdAsync(
                request.OperatorId,
                request.VehicleId.Value,
                cancellationToken) is null)
        {
            throw new CodedNotFoundException("VEHICLE_NOT_FOUND", "Vehicle was not found.");
        }

        await ValidateAssignedUsersAsync(
            request.OperatorId,
            request.DriverUserId,
            request.AssistantUserId,
            cancellationToken);

        if (request.IsActive)
        {
            if (resourceAvailability is null)
            {
                if (await driverScheduleRepository.HasDriverConflictAsync(
                        request.DriverUserId,
                        request.DayOfWeek,
                        request.DepartureTime,
                        request.ValidFrom,
                        request.ValidUntil,
                        cancellationToken: cancellationToken))
                {
                    throw new ConflictException("TRIP_DRIVER_CONFLICT", "Driver has a conflicting active schedule.");
                }

                EnsureRouteCanGenerateTrips(request.RouteId);
            }
            else
            {
                EnsureRouteCanGenerateTrips(request.RouteId);
                ResourceAvailabilityConflictGuard.EnsureAvailable(
                    await resourceAvailability.CheckDriverScheduleAsync(
                        ToAvailabilityInput(request),
                        acquireLocks: true,
                        cancellationToken),
                    AssignmentSourceType.DRIVER_SCHEDULE);
            }
        }

        var dayOfWeek = JsonSerializer.SerializeToElement(request.DayOfWeek);
        var schedule = DriverSchedule.Create(
            request.OperatorId,
            request.RouteId,
            request.VehicleId,
            request.DriverUserId,
            request.AssistantUserId,
            dayOfWeek,
            request.DepartureTime,
            request.ValidFrom,
            request.ValidUntil,
            request.IsActive,
            request.BaseFare.HasValue ? Money.FromRaw(request.BaseFare.Value) : null);

        await driverScheduleRepository.AddAsync(schedule, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (schedule.IsActive)
        {
            tripGenerationJobScheduler.EnqueueScheduleGeneration(schedule.Id);
        }

        return DriverScheduleMapper.ToDto(schedule);
    }

    private static DriverScheduleAvailabilityInput ToAvailabilityInput(CreateDriverScheduleCommand request) =>
        new(
            request.OperatorId,
            request.RouteId,
            request.VehicleId,
            request.DriverUserId,
            request.AssistantUserId,
            request.DayOfWeek,
            request.DepartureTime,
            request.ValidFrom,
            request.ValidUntil);

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

    private void EnsureRouteCanGenerateTrips(Guid routeId)
    {
        var route = routeRepository.QueryNoTracking().FirstOrDefault(route => route.Id == routeId)
            ?? throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Route was not found.");
        if (!route.IsActive || route.DeletedAt is not null)
        {
            throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Route was not found.");
        }

        var routeStops = routeStopRepository.QueryNoTracking()
            .Where(routeStop => routeStop.RouteId == routeId)
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
