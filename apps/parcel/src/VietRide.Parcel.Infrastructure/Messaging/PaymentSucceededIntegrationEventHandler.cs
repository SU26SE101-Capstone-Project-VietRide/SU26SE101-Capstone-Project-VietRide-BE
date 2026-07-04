using MediatR;
using VietRide.Parcel.Application.Features.Parcels.ConfirmPaymentForParcel;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

internal sealed class PaymentSucceededIntegrationEventHandler
    : IIntegrationEventHandler<PaymentSucceededIntegrationEvent>
{
    private readonly IMediator _mediator;

    public PaymentSucceededIntegrationEventHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task HandleAsync(
        PaymentSucceededIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await _mediator.Send(
            new ConfirmPaymentForParcelCommand(
                integrationEvent.PaymentId,
                integrationEvent.ReferenceType,
                integrationEvent.ReferenceId,
                integrationEvent.Amount),
            cancellationToken);
    }
}
