using Hangfire;
using MediatR;
using VietRide.Trip.Application.Features.Internal.Trips.ReleaseExpiredSeatLocks;

namespace VietRide.Trip.Infrastructure.Jobs;

public sealed class ExpiredSeatLockReleaseJob
{
    private readonly ISender sender;

    public ExpiredSeatLockReleaseJob(ISender sender)
    {
        this.sender = sender;
    }

    [Queue("trip")]
    [DisableConcurrentExecution(60)]
    public Task ReleaseExpiredAsync(CancellationToken cancellationToken = default) =>
        sender.Send(new ReleaseExpiredSeatLocksCommand(), cancellationToken);
}
