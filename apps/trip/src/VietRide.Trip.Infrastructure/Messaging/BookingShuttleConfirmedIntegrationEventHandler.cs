using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Messaging;

internal sealed class BookingShuttleConfirmedIntegrationEventHandler
    : IIntegrationEventHandler<BookingShuttleConfirmedIntegrationEvent>
{
    private readonly TripDbContext _db;
    private readonly IUnitOfWork _unitOfWork;

    public BookingShuttleConfirmedIntegrationEventHandler(
        TripDbContext db,
        IUnitOfWork unitOfWork)
    {
        _db = db;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(
        BookingShuttleConfirmedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (integrationEvent.ShuttlePickup is null || integrationEvent.Tickets.Count == 0)
        {
            return;
        }

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var tripExists = await _db.Trips
                .AsNoTracking()
                .AnyAsync(trip => trip.Id == integrationEvent.TripId, cancellationToken);
            if (!tripExists)
            {
                throw new InvalidOperationException($"Main trip '{integrationEvent.TripId}' was not found for shuttle fan-out.");
            }

            foreach (var ticket in integrationEvent.Tickets)
            {
                var passengerUserId = ticket.PassengerUserId ?? integrationEvent.UserId;
                await _db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO vietride_trip.shuttle_passengers (
                        id, main_trip_id, booking_id, ticket_id, passenger_user_id,
                        direction, pickup_address, pickup_lat, pickup_lng,
                        status, created_at, updated_at)
                    VALUES (
                        {Guid.NewGuid()}, {integrationEvent.TripId}, {integrationEvent.BookingId},
                        {ticket.TicketId}, {passengerUserId}, 'INBOUND_TO_STATION',
                        {integrationEvent.ShuttlePickup.Address}, {integrationEvent.ShuttlePickup.Latitude},
                        {integrationEvent.ShuttlePickup.Longitude}, 'PENDING_ASSIGNMENT', now(), now())
                    ON CONFLICT (booking_id, ticket_id)
                        WHERE booking_id IS NOT NULL AND ticket_id IS NOT NULL
                    DO NOTHING
                    """, cancellationToken);
            }

            return true;
        }, cancellationToken);
    }
}
