using MediatR;

namespace VietRide.Trip.Application.Features.Locations;

public sealed record CreateLocationCommand(
    string? Code,
    string? Name,
    string? Type,
    int? SortOrder,
    bool IsActive,
    string? ParentCode = null) : IRequest<LocationDto>;
