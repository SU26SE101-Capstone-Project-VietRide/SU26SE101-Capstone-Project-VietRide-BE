using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Identity.Infrastructure.Messaging;

public sealed class SubscriptionPaymentSucceededIntegrationEventHandler
    : IIntegrationEventHandler<SubscriptionPaymentSucceededIntegrationEvent>
{
    private readonly SubscriptionPaymentActivationService _activation;

    public SubscriptionPaymentSucceededIntegrationEventHandler(
        SubscriptionPaymentActivationService activation)
    {
        _activation = activation;
    }

    public async Task HandleAsync(
        SubscriptionPaymentSucceededIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await _activation.ActivateAsync(
            new SubscriptionPaymentActivationContext(
                integrationEvent.EventId,
                integrationEvent.PaymentId,
                integrationEvent.UpgradeAttemptId,
                integrationEvent.OperatorId,
                integrationEvent.OperatorSubscriptionId,
                integrationEvent.PlanId,
                integrationEvent.Amount,
                integrationEvent.Method,
                integrationEvent.BillingPeriod,
                integrationEvent.PeriodFrom,
                integrationEvent.PeriodTo,
                integrationEvent.SucceededAt == default ? integrationEvent.OccurredAt : integrationEvent.SucceededAt),
            cancellationToken);
    }
}
