using VietRide.Trip.Application.Events;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Services;

public interface ITripRouteChangeService
{
    Task<TripRouteChangeResult> ApplyAsync(
        Domain.Entities.Trip trip,
        AlternativeRoute alternativeRoute,
        IReadOnlyCollection<Guid> affectedBookingIds,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken);
}

public sealed record TripRouteChangeResult(
    Guid TripId,
    string Status,
    Guid AlternativeRouteId,
    IReadOnlyList<TripRouteChangedAffectedBooking> AffectedBookings);
