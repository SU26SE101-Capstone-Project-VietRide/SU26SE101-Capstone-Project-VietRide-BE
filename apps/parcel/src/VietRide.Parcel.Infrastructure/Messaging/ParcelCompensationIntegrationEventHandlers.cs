using MediatR;
using VietRide.Parcel.Application.Features.Reliability.Claims;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

internal sealed class ParcelCompensationPaidIntegrationEventHandler
    : IIntegrationEventHandler<ParcelCompensationPaidIntegrationEvent>
{
    private readonly IMediator _mediator;
    public ParcelCompensationPaidIntegrationEventHandler(IMediator mediator) => _mediator = mediator;

    public async Task HandleAsync(ParcelCompensationPaidIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        => await _mediator.Send(
            new HandleParcelCompensationStatusCommand(
                integrationEvent.ClaimId,
                integrationEvent.PayoutId,
                "PAID",
                integrationEvent.OccurredAt),
            cancellationToken);
}

internal sealed class ParcelCompensationFundingPendingIntegrationEventHandler
    : IIntegrationEventHandler<ParcelCompensationFundingPendingIntegrationEvent>
{
    private readonly IMediator _mediator;
    public ParcelCompensationFundingPendingIntegrationEventHandler(IMediator mediator) => _mediator = mediator;

    public async Task HandleAsync(ParcelCompensationFundingPendingIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        => await _mediator.Send(
            new HandleParcelCompensationStatusCommand(
                integrationEvent.ClaimId,
                integrationEvent.PayoutId,
                "FUNDING_PENDING",
                integrationEvent.OccurredAt),
            cancellationToken);
}
