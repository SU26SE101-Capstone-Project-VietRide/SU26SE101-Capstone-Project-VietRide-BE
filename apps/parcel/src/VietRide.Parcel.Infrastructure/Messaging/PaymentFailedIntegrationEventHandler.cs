using MediatR;
using VietRide.Parcel.Application.Features.Parcels.FailPaymentForParcel;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

internal sealed class PaymentFailedIntegrationEventHandler
    : IIntegrationEventHandler<PaymentFailedIntegrationEvent>
{
    private readonly IMediator _mediator;

    public PaymentFailedIntegrationEventHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task HandleAsync(
        PaymentFailedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await _mediator.Send(
            new FailPaymentForParcelCommand(
                integrationEvent.PaymentId,
                integrationEvent.ReferenceType,
                integrationEvent.ReferenceId),
            cancellationToken);
    }
}
