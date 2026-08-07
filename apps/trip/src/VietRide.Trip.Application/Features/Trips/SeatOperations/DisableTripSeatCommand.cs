using MediatR;
using VietRide.Trip.Application.Features.Trips.GetTripSeatMap;

namespace VietRide.Trip.Application.Features.Trips.SeatOperations;

public sealed record DisableTripSeatCommand(
    Guid TripId,
    Guid OperatorId,
    Guid ActorUserId,
    string SeatNumber,
    string Reason,
    string RequestId) : IRequest<TripSeatMapDto>;
