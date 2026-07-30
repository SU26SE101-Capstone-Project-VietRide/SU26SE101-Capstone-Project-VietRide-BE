using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Features.Parcels.RecoverCargoRecoveryOperations;

namespace VietRide.Parcel.Infrastructure.Jobs;

public sealed class PendingCargoRecoveryOperationJob
{
    public const string RecurringJobId = "parcel.pending-cargo-recovery-operation";

    private readonly IMediator _mediator;
    private readonly ILogger<PendingCargoRecoveryOperationJob> _logger;

    public PendingCargoRecoveryOperationJob(
        IMediator mediator,
        ILogger<PendingCargoRecoveryOperationJob> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var recovered = await _mediator.Send(
            new RecoverCargoRecoveryOperationsCommand(),
            cancellationToken);
        _logger.LogInformation(
            "Pending cargo recovery operation scan completed. Recovered {RecoveredCount} operation(s).",
            recovered);
    }
}
