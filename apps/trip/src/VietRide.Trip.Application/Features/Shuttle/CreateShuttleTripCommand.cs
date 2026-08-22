using MediatR;
using VietRide.Shared.Application.Behaviors;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

[SkipTransaction]
public sealed record CreateShuttleTripCommand(
    Guid OperatorId,
    Guid ActorUserId,
    Guid MainTripId,
    Guid DriverUserId,
    Guid VehicleId,
    DateTimeOffset ScheduledDepartureTime,
    DateTimeOffset ScheduledEndTime,
    IReadOnlyList<Guid> OrderedBookingIds,
    string? Notes,
    string Direction = "INBOUND_TO_STATION") : IRequest<CreateShuttleTripResult>;
