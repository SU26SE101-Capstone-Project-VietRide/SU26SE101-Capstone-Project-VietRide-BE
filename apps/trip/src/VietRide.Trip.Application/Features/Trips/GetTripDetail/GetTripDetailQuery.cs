using MediatR;

namespace VietRide.Trip.Application.Features.Trips.GetTripDetail;

public sealed record GetTripDetailQuery(Guid TripId) : IRequest<TripDetailDto>;
