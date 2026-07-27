using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Features.Parcels.ExpireSettlementTimeouts;

namespace VietRide.Parcel.Infrastructure.Jobs;

public sealed class ParcelSettlementTimeoutJob
{
    public const string RecurringJobId = "parcel.settlement-timeout";

    private readonly IMediator _mediator;
    private readonly ILogger<ParcelSettlementTimeoutJob> _logger;

    public ParcelSettlementTimeoutJob(
        IMediator mediator,
        ILogger<ParcelSettlementTimeoutJob> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var count = await _mediator.Send(
            new ExpireParcelSettlementTimeoutsCommand(),
            cancellationToken);
        _logger.LogInformation(
            "Parcel settlement timeout scan rejected {RejectedCount} parcel(s).",
            count);
    }
}
