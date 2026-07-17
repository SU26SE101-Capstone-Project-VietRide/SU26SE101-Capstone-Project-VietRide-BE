using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

public sealed class StationMergedIntegrationEventHandler : IIntegrationEventHandler<StationMergedIntegrationEvent>
{
    private readonly IBookingStationRedirectRepository _redirects;

    public StationMergedIntegrationEventHandler(IBookingStationRedirectRepository redirects)
        => _redirects = redirects;

    public async Task HandleAsync(
        StationMergedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (integrationEvent.OccurredAt == default)
            throw new InvalidOperationException("Station merge event occurredAt is required.");

        await _redirects.ApplyMergeAsync(
            integrationEvent.EventId,
            new DateTimeOffset(integrationEvent.OccurredAt.ToUniversalTime()),
            integrationEvent.PrimaryStationId,
            integrationEvent.DuplicateStationId,
            cancellationToken);
    }
}
