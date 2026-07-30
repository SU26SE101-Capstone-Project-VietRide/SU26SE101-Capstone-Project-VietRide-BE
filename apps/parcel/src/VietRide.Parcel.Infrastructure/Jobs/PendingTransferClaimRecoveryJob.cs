using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Features.Parcels.RecoverTransferClaims;

namespace VietRide.Parcel.Infrastructure.Jobs;

public sealed class PendingTransferClaimRecoveryJob
{
    public const string RecurringJobId = "parcel.pending-transfer-claim-recovery";

    private readonly IMediator _mediator;
    private readonly ILogger<PendingTransferClaimRecoveryJob> _logger;

    public PendingTransferClaimRecoveryJob(
        IMediator mediator,
        ILogger<PendingTransferClaimRecoveryJob> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var recovered = await _mediator.Send(
            new RecoverTransferClaimsCommand(),
            cancellationToken);
        _logger.LogInformation(
            "Pending transfer claim recovery completed. Recovered {RecoveredCount} Parcel(s).",
            recovered);
    }
}
