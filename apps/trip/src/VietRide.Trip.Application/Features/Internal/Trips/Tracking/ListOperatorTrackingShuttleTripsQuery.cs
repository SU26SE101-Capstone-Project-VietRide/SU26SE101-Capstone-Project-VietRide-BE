using MediatR;
using VietRide.Shared.Application.Cqrs;

namespace VietRide.Trip.Application.Features.Internal.Trips.Tracking;

public sealed record ListOperatorTrackingShuttleTripsQuery(Guid OperatorId)
    : IRequest<IReadOnlyList<OperatorTrackingShuttleTripDto>>,
      IQuery<IReadOnlyList<OperatorTrackingShuttleTripDto>>;
