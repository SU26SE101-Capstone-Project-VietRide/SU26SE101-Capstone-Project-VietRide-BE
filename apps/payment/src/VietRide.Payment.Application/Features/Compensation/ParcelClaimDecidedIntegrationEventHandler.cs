using VietRide.Payment.Application.Events;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Features.Compensation;

public sealed class ParcelClaimDecidedIntegrationEventHandler
    : IIntegrationEventHandler<ParcelClaimDecidedIntegrationEvent>
{
    private readonly ParcelCompensationPayoutService _service;

    public ParcelClaimDecidedIntegrationEventHandler(ParcelCompensationPayoutService service)
    {
        _service = service;
    }

    public Task HandleAsync(
        ParcelClaimDecidedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(integrationEvent.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;
        if (!integrationEvent.TripId.HasValue || integrationEvent.TripId == Guid.Empty)
            throw new InvalidOperationException("An approved parcel claim must include tripId.");
        return _service.ProcessApprovedClaimAsync(
            integrationEvent.EventId,
            integrationEvent.ClaimId,
            integrationEvent.ParcelId,
            integrationEvent.TripId.Value,
            integrationEvent.OperatorId,
            integrationEvent.BeneficiaryUserId,
            integrationEvent.TotalAwardVnd,
            cancellationToken);
    }
}
