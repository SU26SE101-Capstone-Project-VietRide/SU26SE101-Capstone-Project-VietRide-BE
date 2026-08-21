using Hangfire;
using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Features.Compensation;

namespace VietRide.Payment.Infrastructure.Jobs;

public sealed class ParcelCompensationFundingRetryJob
{
    public const string RecurringJobId = "payment.parcel-compensation-funding-retry";
    private readonly ParcelCompensationPayoutService _service;
    private readonly ILogger<ParcelCompensationFundingRetryJob> _logger;

    public ParcelCompensationFundingRetryJob(
        ParcelCompensationPayoutService service,
        ILogger<ParcelCompensationFundingRetryJob> logger)
    {
        _service = service;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var count = await _service.RetryFundingPendingAsync(100, cancellationToken);
        _logger.LogInformation("Retried {Count} funding-pending parcel compensation payout(s).", count);
    }
}
