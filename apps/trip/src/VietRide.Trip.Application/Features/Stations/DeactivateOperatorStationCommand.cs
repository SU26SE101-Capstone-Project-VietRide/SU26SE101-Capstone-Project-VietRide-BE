using MediatR;

namespace VietRide.Trip.Application.Features.Stations;

public sealed record DeactivateOperatorStationCommand(Guid OperatorId, Guid StationId) : IRequest<OperatorStationDto>;
