using VietRide.Shared.Application.Cqrs;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed record GetShuttleTrackingContextQuery(
    Guid ShuttleTripId,
    Guid UserId,
    string Role,
    Guid? OperatorId) : IQuery<ShuttleTrackingContext>;
