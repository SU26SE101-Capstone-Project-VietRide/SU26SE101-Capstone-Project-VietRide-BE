using MediatR;

namespace VietRide.Trip.Application.Features.Stations;

public sealed record UpdateAdminStationCommand(
    Guid StationId,
    string? Name,
    string? AddressStreet,
    Guid? LocationId,
    string? City,
    string? Province,
    decimal? Latitude,
    decimal? Longitude,
    string? ContactPhone,
    string? ContactEmail,
    string? OperatingHours,
    string? Facilities,
    bool? SupportsShuttle,
    bool? IsActive) : IRequest<StationDto>;
