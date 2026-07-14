using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Trip.Application.Features.DriverTrips.StartTrip;

[SkipTransaction]
public sealed record StartTripCommand(Guid TripId, Guid ActorUserId) : IRequest<StartTripResponse>;
