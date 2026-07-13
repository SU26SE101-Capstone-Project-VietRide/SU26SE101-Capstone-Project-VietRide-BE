using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Infrastructure.Messaging;

internal sealed class BookingShuttleCancelledIntegrationEventHandler
    : IIntegrationEventHandler<BookingShuttleCancelledIntegrationEvent>
{
    private readonly TripDbContext _db;

    public BookingShuttleCancelledIntegrationEventHandler(TripDbContext db)
    {
        _db = db;
    }

    public async Task HandleAsync(
        BookingShuttleCancelledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var manifests = await _db.ShuttlePassengers
            .Where(passenger => passenger.BookingId == integrationEvent.BookingId
                && passenger.Status != Domain.Entities.ShuttlePassenger.CancelledStatus
                && passenger.Status != Domain.Entities.ShuttlePassenger.DeliveredStatus)
            .ToArrayAsync(cancellationToken);

        foreach (var manifest in manifests)
        {
            manifest.Cancel("BOOKING_CANCELLED");
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
