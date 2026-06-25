using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Features.Payments.ExpirePayment;

namespace VietRide.Payment.Infrastructure.Jobs;

/// <summary>
/// Recurring Hangfire entry point for expiring stale VNPay booking payments.
/// </summary>
public sealed class PaymentExpiredJob
{
    public const string RecurringJobId = "payment.payment-expired";

    private readonly IMediator _mediator;
    private readonly ILogger<PaymentExpiredJob> _logger;

    public PaymentExpiredJob(IMediator mediator, ILogger<PaymentExpiredJob> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new ExpirePaymentCommand(), cancellationToken);

        _logger.LogInformation(
            "Payment expiration scan completed. Expired {ExpiredCount} payment(s).",
            result.ExpiredCount);
    }
}
