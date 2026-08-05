using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Messaging;

internal sealed class BookingShuttleConfirmedIntegrationEventHandler
    : IIntegrationEventHandler<BookingShuttleConfirmedIntegrationEvent>
{
    private readonly TripDbContext _db;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IShuttleDistanceClient _distanceClient;

    public BookingShuttleConfirmedIntegrationEventHandler(
        TripDbContext db,
        IUnitOfWork unitOfWork,
        IShuttleDistanceClient distanceClient)
    {
        _db = db;
        _unitOfWork = unitOfWork;
        _distanceClient = distanceClient;
    }

    public async Task HandleAsync(
        BookingShuttleConfirmedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var requests = integrationEvent.ShuttleRequests?.Count > 0
            ? integrationEvent.ShuttleRequests
            : integrationEvent.ShuttlePickup is null
                ? []
                : [new BookingShuttleConfirmedIntegrationEvent.ShuttleRequestPayload(
                    ShuttlePassenger.InboundDirection,
                    integrationEvent.ShuttlePickup.Address,
                    integrationEvent.ShuttlePickup.Latitude,
                    integrationEvent.ShuttlePickup.Longitude)];
        if (requests.Count == 0 || integrationEvent.Tickets.Count == 0)
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
                foreach (var request in requests)
                {
                    if (request.Direction is not (ShuttlePassenger.InboundDirection or ShuttlePassenger.OutboundDirection))
                    {
                        throw new InvalidOperationException("Shuttle direction is invalid.");
                    }

                    var roadDistanceMeters = request.RoadDistanceMeters
                        ?? await ResolveLegacyRoadDistanceAsync(integrationEvent.TripId, request, cancellationToken);
                    if (roadDistanceMeters is < 0 or > 5_000)
                    {
                        throw new InvalidOperationException("Shuttle road distance is outside the 5 km limit.");
                    }

                    await _db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO vietride_trip.shuttle_passengers (
                        id, main_trip_id, booking_id, ticket_id, passenger_user_id,
                        direction, pickup_address, pickup_lat, pickup_lng,
                        road_distance_meters, status, created_at, updated_at)
                    VALUES (
                        {Guid.NewGuid()}, {integrationEvent.TripId}, {integrationEvent.BookingId},
                        {ticket.TicketId}, {passengerUserId}, {request.Direction},
                        {request.Address}, {request.Latitude},
                        {request.Longitude}, {roadDistanceMeters}, 'PENDING_ASSIGNMENT', now(), now())
                    ON CONFLICT (booking_id, ticket_id, direction)
                        WHERE booking_id IS NOT NULL AND ticket_id IS NOT NULL
                    DO NOTHING
                    """, cancellationToken);
                }
            }

            return true;
        }, cancellationToken);
    }

    private async Task<int> ResolveLegacyRoadDistanceAsync(
        Guid tripId,
        BookingShuttleConfirmedIntegrationEvent.ShuttleRequestPayload request,
        CancellationToken cancellationToken)
    {
        if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180)
        {
            throw new InvalidOperationException("Legacy shuttle coordinates are invalid.");
        }

        var trip = await _db.Trips.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == tripId,
            cancellationToken)
            ?? throw new InvalidOperationException($"Main trip '{tripId}' was not found for legacy shuttle distance resolution.");
        var route = await _db.Routes.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == trip.RouteId,
            cancellationToken)
            ?? throw new InvalidOperationException($"Route '{trip.RouteId}' was not found for legacy shuttle distance resolution.");
        var stationId = request.Direction == ShuttlePassenger.InboundDirection
            ? route.OriginStationId
            : route.DestinationStationId;
        var station = await _db.Stations.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == stationId,
            cancellationToken)
            ?? throw new InvalidOperationException($"Station '{stationId}' was not found for legacy shuttle distance resolution.");
        if (!station.IsActive || station.DeletedAt.HasValue || !station.SupportsShuttle
            || !station.Latitude.HasValue || !station.Longitude.HasValue)
        {
            throw new InvalidOperationException("Legacy shuttle station is not eligible for shuttle service.");
        }

        var outcome = await _distanceClient.CalculateAsync(
            station.Latitude.Value,
            station.Longitude.Value,
            request.Latitude,
            request.Longitude,
            cancellationToken);
        return outcome switch
        {
            ShuttleDistanceOutcome.Success success => success.DistanceMeters,
            ShuttleDistanceOutcome.Unavailable unavailable => throw new InvalidOperationException(
                $"Legacy shuttle road distance is unavailable: {unavailable.Message}"),
            _ => throw new InvalidOperationException("Legacy shuttle road distance response is invalid."),
        };
    }
}
