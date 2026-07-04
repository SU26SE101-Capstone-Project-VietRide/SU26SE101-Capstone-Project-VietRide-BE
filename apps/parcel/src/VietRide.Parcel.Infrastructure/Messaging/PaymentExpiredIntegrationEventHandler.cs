using MediatR;
using VietRide.Parcel.Application.Features.Parcels.ExpirePaymentForParcel;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

internal sealed class PaymentExpiredIntegrationEventHandler
    : IIntegrationEventHandler<PaymentExpiredIntegrationEvent>
{
    private readonly IMediator _mediator;

    public PaymentExpiredIntegrationEventHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task HandleAsync(
        PaymentExpiredIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await _mediator.Send(
            new ExpirePaymentForParcelCommand(
                integrationEvent.PaymentId,
                integrationEvent.ReferenceType,
                integrationEvent.ReferenceId),
            cancellationToken);
    }
}
