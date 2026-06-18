using MediatR;

namespace VietRide.Trip.Application.Features.Trips.GetTripSeatMap;

public sealed record GetTripSeatMapQuery(Guid TripId) : IRequest<TripSeatMapDto>;
