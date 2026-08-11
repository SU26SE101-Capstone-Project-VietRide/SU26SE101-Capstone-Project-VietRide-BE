using VietRide.Shared.Application.Cqrs;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.ResourceAvailability;

public sealed record CheckShuttleAvailabilityQuery(
    Guid OperatorId,
    Guid MainTripId,
    string Direction,
    Guid DriverUserId,
    Guid VehicleId,
    DateTimeOffset ScheduledDepartureTime,
    DateTimeOffset ScheduledEndTime,
    IReadOnlyList<Guid> OrderedBookingIds) : IQuery<ResourceAvailabilityResult>;
