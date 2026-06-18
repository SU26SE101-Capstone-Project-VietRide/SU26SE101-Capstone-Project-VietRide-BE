using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Features.TopUps.ExpireTopUp;

namespace VietRide.Payment.Infrastructure.Jobs;

/// <summary>
/// Recurring Hangfire entry point for expiring stale VNPay top-up requests.
/// </summary>
public sealed class TopUpExpiredJob
{
    public const string RecurringJobId = "payment.top-up-expired";

    private readonly IMediator _mediator;
    private readonly ILogger<TopUpExpiredJob> _logger;

    public TopUpExpiredJob(IMediator mediator, ILogger<TopUpExpiredJob> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new ExpireTopUpCommand(), cancellationToken);

        _logger.LogInformation(
            "Top-up expiration scan completed. Expired {ExpiredCount} request(s).",
            result.ExpiredCount);
    }
}
