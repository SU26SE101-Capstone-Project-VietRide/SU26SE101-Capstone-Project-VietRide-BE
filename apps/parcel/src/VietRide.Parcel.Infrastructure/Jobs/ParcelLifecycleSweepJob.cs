using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Features.Parcels.SweepLifecycle;

namespace VietRide.Parcel.Infrastructure.Jobs;

public sealed class ParcelLifecycleSweepJob
{
    public const string RecurringJobId = "parcel.lifecycle-sweep";

    private readonly IMediator _mediator;
    private readonly ILogger<ParcelLifecycleSweepJob> _logger;

    public ParcelLifecycleSweepJob(IMediator mediator, ILogger<ParcelLifecycleSweepJob> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var count = await _mediator.Send(new ParcelLifecycleSweepCommand(), cancellationToken);

        _logger.LogInformation("Parcel lifecycle sweep completed. Processed {ProcessedCount} parcel(s).", count);
    }
}
