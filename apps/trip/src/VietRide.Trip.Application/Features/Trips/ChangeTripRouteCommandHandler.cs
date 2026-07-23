using System.Text.Json;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Events;

namespace VietRide.Trip.Application.Features.Trips;

public sealed class ChangeTripRouteCommandHandler : IRequestHandler<ChangeTripRouteCommand, ChangeTripRouteResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ITripRepository trips;
    private readonly IAlternativeRouteRepository alternativeRoutes;
    private readonly IBookingImpactClient bookingImpact;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IUnitOfWork unitOfWork;
    private readonly IClock clock;

    public ChangeTripRouteCommandHandler(
        ITripRepository trips,
        IAlternativeRouteRepository alternativeRoutes,
        IBookingImpactClient bookingImpact,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        this.trips = trips;
        this.alternativeRoutes = alternativeRoutes;
        this.bookingImpact = bookingImpact;
        this.outbox = outbox;
        this.unitOfWork = unitOfWork;
        this.clock = clock;
    }

    public async Task<ChangeTripRouteResponse> Handle(ChangeTripRouteCommand request, CancellationToken cancellationToken)
    {
        var trip = await trips.GetRouteChangePreflightAsync(request.TripId, cancellationToken);
        if (trip is null || trip.OperatorId != request.OperatorId)
            throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");

        EnsureRouteChangeAllowed(trip);
        var alternative = await alternativeRoutes.GetOwnedByIdAsync(
            request.OperatorId, request.AlternativeRouteId, cancellationToken);
        if (alternative is null || !alternative.IsActive || alternative.RouteId != trip.RouteId)
            throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Alternative route was not found.");

        var projection = await bookingImpact.GetTripEditImpactAsync(
            trip.Id, request.OperatorId, cancellationToken);
        var affected = projection.ActiveBookings
            .Select(booking => booking.BookingId)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var lockedTrip = await trips.AcquireForRouteChangeAsync(request.TripId, cancellationToken);
            if (lockedTrip is null || lockedTrip.OperatorId != request.OperatorId)
                throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
            EnsureRouteChangeAllowed(lockedTrip);
            var lockedAlternative = await alternativeRoutes.AcquireOwnedByIdAsync(
                request.OperatorId, request.AlternativeRouteId, cancellationToken);
            if (lockedAlternative is null
                || !lockedAlternative.IsActive
                || lockedAlternative.RouteId != lockedTrip.RouteId)
            {
                throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Alternative route was not found.");
            }
            if (!lockedTrip.ChangeAlternativeRoute(request.AlternativeRouteId))
            {
                await unitOfWork.CommitAsync(cancellationToken);
                return new ChangeTripRouteResponse(lockedTrip.Id, lockedTrip.Status.ToString(), request.AlternativeRouteId, affected);
            }

            var evt = new TripRouteChangedIntegrationEvent(
                lockedTrip.Id,
                lockedTrip.OperatorId,
                request.AlternativeRouteId,
                affected,
                clock.UtcNow);
            await outbox.EnqueueAsync(
                evt.EventId,
                evt.EventType,
                JsonSerializer.Serialize(evt, JsonOptions),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return new ChangeTripRouteResponse(lockedTrip.Id, lockedTrip.Status.ToString(), request.AlternativeRouteId, affected);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static void EnsureRouteChangeAllowed(Domain.Entities.Trip trip)
    {
        try
        {
            trip.EnsureAlternativeRouteChangeAllowed();
        }
        catch (InvalidOperationException exception)
        {
            throw new CodedConflictException("TRIP_NOT_EDITABLE", exception.Message);
        }
    }
}
