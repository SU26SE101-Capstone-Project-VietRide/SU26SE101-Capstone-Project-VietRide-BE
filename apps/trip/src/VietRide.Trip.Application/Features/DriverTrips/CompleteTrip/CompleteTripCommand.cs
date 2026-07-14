using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Trip.Application.Features.DriverTrips.CompleteTrip;

[SkipTransaction]
public sealed record CompleteTripCommand(
    Guid TripId,
    Guid ActorUserId,
    string ActorRole) : IRequest<CompleteTripResponse>;
