using MediatR;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed record CompleteTripCommand(
    Guid TripId,
    Guid? ActorUserId,
    bool IsAutomatic = false) : IRequest<CompleteTripResponse>;

public sealed record CompleteTripResponse(
    Guid TripId,
    string Status,
    DateTimeOffset CompletedAt);
