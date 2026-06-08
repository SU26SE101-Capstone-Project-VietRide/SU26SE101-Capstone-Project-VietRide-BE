using MediatR;

namespace VietRide.Trip.Application.Features.Stations;

public sealed record CreateOrLinkOperatorStationCommand(
    Guid OperatorId,
    Guid? StationId,
    string? Name,
    string? City,
    string? Province,
    decimal? Latitude,
    decimal? Longitude,
    string? AddressStreet,
    string? StationContactPhone,
    string? ContactEmail,
    string? OperatingHours,
    string? Facilities,
    bool SupportsShuttle,
    string? DisplayNameOverride,
    string? CounterLocation,
    string? OperatorStationContactPhone,
    string? Instructions) : IRequest<CreateOrLinkOperatorStationResponse>;
