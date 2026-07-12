using Microsoft.Extensions.Logging;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Identity.Infrastructure.Messaging;

public sealed class SubscriptionPaymentSucceededIntegrationEventHandler
    : IIntegrationEventHandler<SubscriptionPaymentSucceededIntegrationEvent>
{
    private readonly ISubscriptionUpgradeAttemptRepository _attempts;
    private readonly IOperatorSubscriptionRepository _subscriptions;
    private readonly IClock _clock;
    private readonly ILogger<SubscriptionPaymentSucceededIntegrationEventHandler> _logger;

    public SubscriptionPaymentSucceededIntegrationEventHandler(
        ISubscriptionUpgradeAttemptRepository attempts,
        IOperatorSubscriptionRepository subscriptions,
        IClock clock,
        ILogger<SubscriptionPaymentSucceededIntegrationEventHandler> logger)
    {
        _attempts = attempts;
        _subscriptions = subscriptions;
        _clock = clock;
        _logger = logger;
    }

    public async Task HandleAsync(
        SubscriptionPaymentSucceededIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var attempt = await _attempts.GetByIdAsync(integrationEvent.UpgradeAttemptId, cancellationToken);
        if (attempt is null
            || attempt.OperatorId != integrationEvent.OperatorId
            || attempt.PaymentId != integrationEvent.PaymentId
            || attempt.Amount.Amount != integrationEvent.Amount)
        {
            _logger.LogWarning(
                "Ignoring invalid subscription payment event {EventId} for upgrade attempt {UpgradeAttemptId}.",
                integrationEvent.EventId,
                integrationEvent.UpgradeAttemptId);
            return;
        }

        if (attempt.Status == SubscriptionUpgradeAttemptStatus.SUCCEEDED)
            return;
        if (attempt.Status != SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING)
        {
            _logger.LogWarning(
                "Ignoring subscription payment event {EventId}; upgrade attempt {UpgradeAttemptId} is {Status}.",
                integrationEvent.EventId,
                attempt.Id,
                attempt.Status);
            return;
        }

        var subscription = await _subscriptions.GetByIdAsync(attempt.SubscriptionId, cancellationToken);
        if (subscription is null)
        {
            _logger.LogWarning(
                "Ignoring subscription payment event {EventId}; subscription {SubscriptionId} was not found.",
                integrationEvent.EventId,
                attempt.SubscriptionId);
            return;
        }

        subscription.ActivatePaid(attempt.TargetPlanId, attempt.BillingPeriod, _clock.UtcNow);
        attempt.MarkSucceeded(integrationEvent.PaymentId);
        _subscriptions.Update(subscription);
        _attempts.Update(attempt);
    }
}
