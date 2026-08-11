using MediatR;

namespace VietRide.Trip.Application.Features.Stations;

public sealed record SearchStationsQuery(
    string? Q,
    string? City,
    string? Ward,
    Guid? LocationId,
    string? LocationScopeCode = null) : IRequest<IReadOnlyList<StationSearchResult>>;
