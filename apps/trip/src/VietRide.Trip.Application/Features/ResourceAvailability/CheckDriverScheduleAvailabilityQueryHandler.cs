using MediatR;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.ResourceAvailability;

public sealed class CheckDriverScheduleAvailabilityQueryHandler
    : IRequestHandler<CheckDriverScheduleAvailabilityQuery, ResourceAvailabilityResult>
{
    private readonly IResourceAvailabilityService availability;

    public CheckDriverScheduleAvailabilityQueryHandler(IResourceAvailabilityService availability)
    {
        this.availability = availability;
    }

    public Task<ResourceAvailabilityResult> Handle(
        CheckDriverScheduleAvailabilityQuery request,
        CancellationToken cancellationToken) =>
        availability.CheckDriverScheduleAsync(
            new DriverScheduleAvailabilityInput(
                request.OperatorId,
                request.RouteId,
                request.VehicleId,
                request.DriverUserId,
                request.AssistantUserId,
                request.DayOfWeek,
                request.DepartureTime,
                request.ValidFrom,
                request.ValidUntil),
            acquireLocks: false,
            cancellationToken);
}
