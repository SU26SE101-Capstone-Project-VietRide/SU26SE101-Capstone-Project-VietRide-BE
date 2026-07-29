using MediatR;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Trips.ListOperatorTrips;

public sealed record ListOperatorTripsQuery(
    Guid OperatorId,
    string? Search,
    TripStatus? Status,
    DateOnly? From,
    DateOnly? To,
    int? Page,
    int? PageSize,
    string? SortBy,
    string? SortDir)
    : IRequest<PagedResult<OperatorTripListItemDto>>;
