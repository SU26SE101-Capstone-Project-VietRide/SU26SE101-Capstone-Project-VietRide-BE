using MediatR;

namespace VietRide.Trip.Application.Features.Internal.Trips.ReleaseExpiredSeatLocks;

public sealed record ReleaseExpiredSeatLocksCommand(int BatchSize = 500) : IRequest<int>;
