using VietRide.Shared.Application.Cqrs;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed record GetDriverShuttleAssignmentsQuery(
    Guid DriverUserId,
    DateOnly? From,
    DateOnly? To) : IQuery<ShuttleDriverAssignmentPage>;
