using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

public sealed class IdentityUserDeletedIntegrationEventHandler
    : IIntegrationEventHandler<IdentityUserDeletedIntegrationEvent>
{
    private readonly IBookingRepository bookings;

    public IdentityUserDeletedIntegrationEventHandler(IBookingRepository bookings)
    {
        this.bookings = bookings;
    }

    public async Task HandleAsync(
        IdentityUserDeletedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.UserId == Guid.Empty)
        {
            throw new ArgumentException("Identity user-deleted event requires a user id.");
        }

        await bookings.RedactBuyerSnapshotsAsync(
            integrationEvent.UserId,
            cancellationToken);
    }
}
