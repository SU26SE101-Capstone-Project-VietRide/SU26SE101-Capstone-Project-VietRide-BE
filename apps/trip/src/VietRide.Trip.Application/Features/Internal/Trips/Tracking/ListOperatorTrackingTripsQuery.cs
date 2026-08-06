using MediatR;
using VietRide.Shared.Application.Cqrs;

namespace VietRide.Trip.Application.Features.Internal.Trips.Tracking;

public sealed record ListOperatorTrackingTripsQuery(Guid OperatorId, string? Status)
    : IRequest<IReadOnlyList<OperatorTrackingTripDto>>, IQuery<IReadOnlyList<OperatorTrackingTripDto>>;
