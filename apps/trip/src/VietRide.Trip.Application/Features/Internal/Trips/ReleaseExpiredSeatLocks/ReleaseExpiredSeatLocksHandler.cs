using MediatR;
using VietRide.Trip.Application.Abstractions.SeatLock;

namespace VietRide.Trip.Application.Features.Internal.Trips.ReleaseExpiredSeatLocks;

public sealed class ReleaseExpiredSeatLocksHandler : IRequestHandler<ReleaseExpiredSeatLocksCommand, int>
{
    private readonly IExpiredSeatLockReleaser releaser;

    public ReleaseExpiredSeatLocksHandler(IExpiredSeatLockReleaser releaser)
    {
        this.releaser = releaser;
    }

    public Task<int> Handle(ReleaseExpiredSeatLocksCommand request, CancellationToken cancellationToken) =>
        releaser.ReleaseExpiredAsync(request.BatchSize, cancellationToken);
}
