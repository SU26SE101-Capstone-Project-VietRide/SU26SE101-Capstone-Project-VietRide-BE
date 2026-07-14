using MediatR;

namespace VietRide.Trip.Application.Features.Stations;

public sealed record UpdateOperatorStationCommand(
    Guid OperatorId,
    Guid StationId,
    string? DisplayNameOverride,
    string? CounterLocation,
    string? ContactPhone,
    string? Instructions) : IRequest<OperatorStationDto>;
