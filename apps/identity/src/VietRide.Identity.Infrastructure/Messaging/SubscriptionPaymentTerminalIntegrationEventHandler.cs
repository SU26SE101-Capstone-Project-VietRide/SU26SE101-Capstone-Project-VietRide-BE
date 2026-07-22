using Microsoft.Extensions.Logging;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Identity.Infrastructure.Messaging;

public sealed class SubscriptionPaymentTerminalIntegrationEventHandler
    : IIntegrationEventHandler<SubscriptionPaymentFailedIntegrationEvent>,
      IIntegrationEventHandler<SubscriptionPaymentExpiredIntegrationEvent>
{
    private readonly ISubscriptionUpgradeAttemptRepository _attempts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SubscriptionPaymentTerminalIntegrationEventHandler> _logger;

    public SubscriptionPaymentTerminalIntegrationEventHandler(
        ISubscriptionUpgradeAttemptRepository attempts,
        IUnitOfWork unitOfWork,
        ILogger<SubscriptionPaymentTerminalIntegrationEventHandler> logger)
    {
        _attempts = attempts;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public Task HandleAsync(SubscriptionPaymentFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        => UpdateAsync(
            integrationEvent.EventId,
            integrationEvent.PaymentId,
            integrationEvent.UpgradeAttemptId,
            integrationEvent.OperatorId,
            integrationEvent.OperatorSubscriptionId,
            SubscriptionPaymentSessionStatus.FAILED,
            cancellationToken);

    public Task HandleAsync(SubscriptionPaymentExpiredIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        => UpdateAsync(
            integrationEvent.EventId,
            integrationEvent.PaymentId,
            integrationEvent.UpgradeAttemptId,
            integrationEvent.OperatorId,
            integrationEvent.OperatorSubscriptionId,
            SubscriptionPaymentSessionStatus.EXPIRED,
            cancellationToken);

    private async Task UpdateAsync(
        Guid eventId,
        Guid paymentId,
        Guid attemptId,
        Guid operatorId,
        Guid subscriptionId,
        SubscriptionPaymentSessionStatus status,
        CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var attempt = await _attempts.GetByIdForUpdateAsync(attemptId, cancellationToken);
            if (attempt is null
                || attempt.OperatorId != operatorId
                || attempt.SubscriptionId != subscriptionId
                || attempt.PaymentId != paymentId
                || attempt.Status != SubscriptionUpgradeAttemptStatus.PAYMENT_PENDING)
            {
                _logger.LogWarning(
                    "Quarantining subscription payment terminal event {EventId} for payment {PaymentId} and attempt {UpgradeAttemptId} because its context does not match.",
                    eventId,
                    paymentId,
                    attemptId);
                await _unitOfWork.RollbackAsync(cancellationToken);
                return;
            }

            if (status == SubscriptionPaymentSessionStatus.FAILED)
                attempt.MarkPaymentFailed(paymentId);
            else
                attempt.MarkPaymentExpired(paymentId);
            _attempts.Update(attempt);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
