using MediatR;

namespace VietRide.Trip.Application.Features.Trips.GetOperatorCargoCapacity;

public sealed record GetOperatorCargoCapacityQuery(
    Guid TripId,
    Guid OperatorId) : IRequest<OperatorCargoCapacityDto>;
