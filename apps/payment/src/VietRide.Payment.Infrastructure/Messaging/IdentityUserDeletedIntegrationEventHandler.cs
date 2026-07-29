using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Infrastructure.Messaging;

public sealed class IdentityUserDeletedIntegrationEventHandler
    : IIntegrationEventHandler<IdentityUserDeletedIntegrationEvent>
{
    private readonly IFinancialActorPrivacyStore _privacy;

    public IdentityUserDeletedIntegrationEventHandler(IFinancialActorPrivacyStore privacy)
    {
        _privacy = privacy;
    }

    public async Task HandleAsync(
        IdentityUserDeletedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.UserId == Guid.Empty)
            throw new ArgumentException("Identity user-deleted event requires a user id.");

        await _privacy.MarkDeletedAndRedactAsync(
            integrationEvent.UserId,
            cancellationToken);
    }
}
