using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Internal.Trips.Tracking;

public sealed class GetTripTrackingAuthorizationHandler
    : IRequestHandler<GetTripTrackingAuthorizationQuery, TrackingAuthorizationResponse>
{
    private readonly ITripRepository tripRepository;

    public GetTripTrackingAuthorizationHandler(ITripRepository tripRepository)
    {
        this.tripRepository = tripRepository;
    }

    public async Task<TrackingAuthorizationResponse> Handle(
        GetTripTrackingAuthorizationQuery request,
        CancellationToken cancellationToken)
    {
        var trip = await tripRepository.QueryNoTracking()
            .FirstOrDefaultAsync(trip => trip.Id == request.TripId, cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");

        var role = request.Role?.Trim().ToUpperInvariant();
        var response = role switch
        {
            "DRIVER" when request.UserId == trip.DriverUserId =>
                new TrackingAuthorizationResponse(true, "DRIVER"),
            "ASSISTANT" when request.UserId.HasValue && trip.AssistantUserId == request.UserId.Value =>
                new TrackingAuthorizationResponse(true, "ASSISTANT"),
            "OPERATOR_ADMIN" or "OPERATOR_STAFF" when request.OperatorId == trip.OperatorId =>
                new TrackingAuthorizationResponse(true, "OPERATOR"),
            _ => new TrackingAuthorizationResponse(false, Error: "ACCESS_DENIED"),
        };

        return response;
    }
}
