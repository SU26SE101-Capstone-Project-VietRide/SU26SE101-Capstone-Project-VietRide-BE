using VietRide.Shared.Application.Cqrs;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed record PreviewShuttleRouteQuery(
    Guid OperatorId,
    Guid MainTripId,
    string Direction,
    DateTimeOffset ScheduledDepartureTime,
    IReadOnlyList<Guid> OrderedBookingIds) : IQuery<ShuttleRoutePreviewResult>;
