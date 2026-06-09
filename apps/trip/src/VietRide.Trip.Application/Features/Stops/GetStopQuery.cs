using MediatR;

namespace VietRide.Trip.Application.Features.Stops;

public sealed record GetStopQuery(Guid OperatorId, Guid StopId) : IRequest<StopDto>;
