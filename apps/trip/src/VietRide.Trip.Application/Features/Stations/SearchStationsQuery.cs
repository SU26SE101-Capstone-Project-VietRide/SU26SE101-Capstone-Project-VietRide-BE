using MediatR;

namespace VietRide.Trip.Application.Features.Stations;

public sealed record SearchStationsQuery(
    string? Q,
    string? City,
    string? Province) : IRequest<IReadOnlyList<StationSearchResult>>;
