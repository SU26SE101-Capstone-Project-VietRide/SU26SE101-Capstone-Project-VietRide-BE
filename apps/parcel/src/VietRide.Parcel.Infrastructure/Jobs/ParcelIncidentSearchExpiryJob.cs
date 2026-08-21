using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Features.Reliability.Incidents;

namespace VietRide.Parcel.Infrastructure.Jobs;

public sealed class ParcelIncidentSearchExpiryJob
{
    public const string RecurringJobId = "parcel.incident-search-expiry";
    private readonly IMediator _mediator;
    private readonly ILogger<ParcelIncidentSearchExpiryJob> _logger;

    public ParcelIncidentSearchExpiryJob(IMediator mediator, ILogger<ParcelIncidentSearchExpiryJob> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var count = await _mediator.Send(new ExpireParcelIncidentSearchesCommand(), cancellationToken);
        _logger.LogInformation("Parcel incident search expiry processed {Count} incident(s).", count);
    }
}
