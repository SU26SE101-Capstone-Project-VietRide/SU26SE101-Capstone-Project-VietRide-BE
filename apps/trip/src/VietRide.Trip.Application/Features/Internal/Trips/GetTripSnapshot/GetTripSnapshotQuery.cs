using MediatR;

namespace VietRide.Trip.Application.Features.Internal.Trips.GetTripSnapshot;

public sealed record GetTripSnapshotQuery(Guid TripId) : IRequest<InternalTripSnapshotDto>;
