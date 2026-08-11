using MediatR;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.ResourceAvailability;

public sealed class CheckShuttleAvailabilityQueryHandler
    : IRequestHandler<CheckShuttleAvailabilityQuery, ResourceAvailabilityResult>
{
    private readonly IResourceAvailabilityService availability;

    public CheckShuttleAvailabilityQueryHandler(IResourceAvailabilityService availability)
    {
        this.availability = availability;
    }

    public Task<ResourceAvailabilityResult> Handle(
        CheckShuttleAvailabilityQuery request,
        CancellationToken cancellationToken) =>
        availability.CheckShuttleAsync(
            new ShuttleAvailabilityInput(
                request.OperatorId,
                request.MainTripId,
                request.Direction,
                request.DriverUserId,
                request.VehicleId,
                request.ScheduledDepartureTime,
                request.ScheduledEndTime,
                request.OrderedBookingIds),
            acquireLocks: false,
            cancellationToken);
}
