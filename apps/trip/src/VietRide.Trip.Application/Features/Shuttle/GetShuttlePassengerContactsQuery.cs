using VietRide.Shared.Application.Cqrs;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed record GetShuttlePassengerContactsQuery(Guid OperatorId, Guid ShuttleTripId)
    : IQuery<ShuttlePassengerContactResponse>;
