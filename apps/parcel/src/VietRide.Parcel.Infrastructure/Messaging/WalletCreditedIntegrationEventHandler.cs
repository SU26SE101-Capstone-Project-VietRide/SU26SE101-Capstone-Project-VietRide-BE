using MediatR;
using VietRide.Parcel.Application.Features.Parcels.RecordRefund;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

internal sealed class WalletCreditedIntegrationEventHandler
    : IIntegrationEventHandler<WalletCreditedIntegrationEvent>
{
    private readonly IMediator _mediator;

    public WalletCreditedIntegrationEventHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task HandleAsync(
        WalletCreditedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await _mediator.Send(
            new RecordParcelRefundedCommand(
                integrationEvent.ReferenceId,
                integrationEvent.UserId,
                integrationEvent.Amount,
                integrationEvent.ReferenceType),
            cancellationToken);
    }
}
