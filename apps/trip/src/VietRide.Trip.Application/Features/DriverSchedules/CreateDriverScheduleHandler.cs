using System.Text.Json;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Routes;
using VietRide.Trip.Application.Features.Stops;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.DriverSchedules;

public sealed class CreateDriverScheduleHandler : IRequestHandler<CreateDriverScheduleCommand, DriverScheduleDto>
{
    private readonly IDriverScheduleRepository driverScheduleRepository;
    private readonly IIdentityInternalClient identityInternalClient;
    private readonly IRouteRepository routeRepository;
    private readonly IUnitOfWork unitOfWork;
    private readonly IVehicleRepository vehicleRepository;

    public CreateDriverScheduleHandler(
        IDriverScheduleRepository driverScheduleRepository,
        IIdentityInternalClient identityInternalClient,
        IRouteRepository routeRepository,
        IVehicleRepository vehicleRepository,
        IUnitOfWork unitOfWork)
    {
        this.driverScheduleRepository = driverScheduleRepository;
        this.identityInternalClient = identityInternalClient;
        this.routeRepository = routeRepository;
        this.vehicleRepository = vehicleRepository;
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

        if (await driverScheduleRepository.HasDriverConflictAsync(
                request.DriverUserId,
                request.DayOfWeek,
                request.DepartureTime,
                request.ValidFrom,
                request.ValidUntil,
                cancellationToken))
        {
            throw new ConflictException("TRIP_DRIVER_CONFLICT", "Driver has a conflicting active schedule.");
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
            request.ValidUntil);

        await driverScheduleRepository.AddAsync(schedule, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return DriverScheduleMapper.ToDto(schedule);
    }
}
