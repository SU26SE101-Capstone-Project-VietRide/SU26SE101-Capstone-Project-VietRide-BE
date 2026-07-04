using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Events;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Features.Internal.Wallets.RefundToWallet;

public sealed class ParcelRefundInitiatedIntegrationEventHandler : IIntegrationEventHandler<ParcelRefundInitiatedIntegrationEvent>
{
    private readonly MediatR.IMediator _mediator;
    private readonly ILogger<ParcelRefundInitiatedIntegrationEventHandler> _logger;

    public ParcelRefundInitiatedIntegrationEventHandler(
        MediatR.IMediator mediator,
        ILogger<ParcelRefundInitiatedIntegrationEventHandler> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task HandleAsync(ParcelRefundInitiatedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (!string.Equals(integrationEvent.ReferenceType, "PARCEL_REFUND", StringComparison.Ordinal)
            || integrationEvent.ReferenceId != integrationEvent.ParcelId)
        {
            _logger.LogWarning(
                "Dropping invalid parcel.refund.initiated payload for parcel {ParcelId}: referenceType={ReferenceType}, referenceId={ReferenceId}.",
                integrationEvent.ParcelId,
                integrationEvent.ReferenceType,
                integrationEvent.ReferenceId);
            return;
        }

        await _mediator.Send(
            new RefundToWalletCommand(
                integrationEvent.SenderUserId,
                integrationEvent.Amount,
                integrationEvent.ReferenceType,
                integrationEvent.ReferenceId,
                integrationEvent.IdempotencyKey),
            cancellationToken);
    }
}
