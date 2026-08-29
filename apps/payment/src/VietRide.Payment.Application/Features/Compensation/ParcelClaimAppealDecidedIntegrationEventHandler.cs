using VietRide.Payment.Application.Events;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Features.Compensation;

public sealed class ParcelClaimAppealDecidedIntegrationEventHandler
    : IIntegrationEventHandler<ParcelClaimAppealDecidedIntegrationEvent>
{
    private readonly ParcelCompensationPayoutService _service;

    public ParcelClaimAppealDecidedIntegrationEventHandler(ParcelCompensationPayoutService service)
    {
        _service = service;
    }

    public Task HandleAsync(
        ParcelClaimAppealDecidedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                integrationEvent.Status,
                "ADJUSTMENT_APPROVED",
                StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;
        if (!integrationEvent.TripId.HasValue || integrationEvent.TripId == Guid.Empty)
            throw new InvalidOperationException("An approved Parcel claim appeal must include tripId.");
        if (integrationEvent.SupplementaryAwardVnd <= 0)
            throw new InvalidOperationException("An approved Parcel claim appeal must have a positive supplementary award.");

        // AppealId is the unique compensation reference. This keeps the original claim payout immutable
        // and guarantees that retries cannot credit the supplementary award twice.
        return _service.ProcessApprovedClaimAsync(
            integrationEvent.EventId,
            integrationEvent.AppealId,
            integrationEvent.ParcelId,
            integrationEvent.TripId.Value,
            integrationEvent.OperatorId,
            integrationEvent.BeneficiaryUserId,
            integrationEvent.SupplementaryAwardVnd,
            cancellationToken);
    }
}
