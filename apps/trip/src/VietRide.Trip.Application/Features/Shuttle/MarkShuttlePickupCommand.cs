using MediatR;
using VietRide.Shared.Application.Behaviors;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

[SkipTransaction]
public sealed record MarkShuttlePickupCommand(
    Guid ShuttleTripId,
    int PickupOrder,
    Guid DriverUserId) : IRequest<ShuttlePickupResult>;
