using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Trips;

public sealed class ChangeTripRouteCommandHandler : IRequestHandler<ChangeTripRouteCommand, ChangeTripRouteResponse>
{
    private readonly ITripRepository trips;
    private readonly IAlternativeRouteRepository alternativeRoutes;
    private readonly IBookingImpactClient bookingImpact;
    private readonly ITripRouteChangeService tripRouteChanges;
    private readonly IUnitOfWork unitOfWork;
    private readonly IClock clock;
    private readonly IRouteChangeProposalLifecycleService? routeChangeProposals;

    public ChangeTripRouteCommandHandler(
        ITripRepository trips,
        IAlternativeRouteRepository alternativeRoutes,
        IBookingImpactClient bookingImpact,
        ITripRouteChangeService tripRouteChanges,
        IUnitOfWork unitOfWork,
        IClock clock,
        IRouteChangeProposalLifecycleService? routeChangeProposals = null)
    {
        this.trips = trips;
        this.alternativeRoutes = alternativeRoutes;
        this.bookingImpact = bookingImpact;
        this.tripRouteChanges = tripRouteChanges;
        this.unitOfWork = unitOfWork;
        this.clock = clock;
        this.routeChangeProposals = routeChangeProposals;
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
        var affectedBookingIds = projection.ActiveBookings
            .Select(booking => booking.BookingId)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var lockedTrip = await trips.AcquireForRouteChangeAsync(request.TripId, cancellationToken);
            if (lockedTrip is null || lockedTrip.OperatorId != request.OperatorId)
                throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
            EnsureRouteChangeAllowed(lockedTrip);
            var now = clock.UtcNow;
            if (routeChangeProposals is not null)
                await routeChangeProposals.SupersedePendingAsync(lockedTrip.Id, request.ActorUserId, null, now, cancellationToken);
            var lockedAlternative = await alternativeRoutes.AcquireOwnedByIdAsync(
                request.OperatorId, request.AlternativeRouteId, cancellationToken);
            if (lockedAlternative is null
                || !lockedAlternative.IsActive
                || lockedAlternative.RouteId != lockedTrip.RouteId)
            {
                throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Alternative route was not found.");
            }
            var result = await tripRouteChanges.ApplyAsync(
                lockedTrip,
                lockedAlternative,
                affectedBookingIds,
                now,
                cancellationToken);
            return new ChangeTripRouteResponse(
                result.TripId,
                result.Status,
                result.AlternativeRouteId,
                result.AffectedBookings);
        }, cancellationToken);
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
