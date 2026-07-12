using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.DriverTrips.GetAssignedTripRoute;

public sealed class GetAssignedTripRouteHandler
    : IRequestHandler<GetAssignedTripRouteQuery, DriverTripRouteDto>
{
    private readonly ITripRepository tripRepository;

    public GetAssignedTripRouteHandler(ITripRepository tripRepository)
    {
        this.tripRepository = tripRepository;
    }

    public async Task<DriverTripRouteDto> Handle(
        GetAssignedTripRouteQuery request,
        CancellationToken cancellationToken)
    {
        var trip = await tripRepository.GetByIdAsync(request.TripId, cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");

        if (trip.DriverUserId != request.UserId && trip.AssistantUserId != request.UserId)
        {
            throw new ForbiddenException("FORBIDDEN", "Caller is not assigned to this trip.");
        }

        return await tripRepository.GetDriverTripRouteAsync(request.TripId, cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip route was not found.");
    }
}
