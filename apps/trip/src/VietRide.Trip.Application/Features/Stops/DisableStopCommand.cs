using MediatR;

namespace VietRide.Trip.Application.Features.Stops;

public sealed record DisableStopCommand(Guid? OperatorId, Guid StopId, Guid? ReplacedByStopId)
    : IRequest<DisableStopResponse>;

public sealed record DisableStopResponse(
    StopDto Stop,
    string? Warning);
