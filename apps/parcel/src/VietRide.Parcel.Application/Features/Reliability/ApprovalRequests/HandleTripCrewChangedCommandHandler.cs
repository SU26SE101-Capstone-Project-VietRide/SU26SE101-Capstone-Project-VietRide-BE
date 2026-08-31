using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Services;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Reliability.ApprovalRequests;

public sealed class HandleTripCrewChangedCommandHandler
    : IRequestHandler<HandleTripCrewChangedCommand, int>
{
    private readonly IParcelCustodyExceptionRequestRepository _custodyRequests;
    private readonly IParcelStopDepartureApprovalRepository _departureRequests;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public HandleTripCrewChangedCommandHandler(
        IParcelCustodyExceptionRequestRepository custodyRequests,
        IParcelStopDepartureApprovalRepository departureRequests,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _custodyRequests = custodyRequests;
        _departureRequests = departureRequests;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<int> Handle(
        HandleTripCrewChangedCommand command,
        CancellationToken cancellationToken)
    {
        if (command.OldDriverUserId == command.DriverUserId)
            return 0;

        var custody = await _custodyRequests.ListPendingByTripForUpdateAsync(
            command.TripId,
            cancellationToken);
        var departures = await _departureRequests.ListPendingByTripForUpdateAsync(
            command.TripId,
            null,
            cancellationToken);
        var now = _clock.UtcNow;

        if (command.DriverUserId is not Guid targetDriverUserId)
        {
            foreach (var request in custody)
                request.CancelAsInvalidated(now, "Trip no longer has an assigned Driver.");
            foreach (var request in departures)
                request.CancelAsSuperseded(now);
            return custody.Count + departures.Count;
        }

        foreach (var request in custody)
        {
            await ParcelApprovalRequestedEvent.EnqueueAsync(
                _outbox,
                request.Id,
                "CUSTODY_EXCEPTION",
                command.OperatorId,
                targetDriverUserId,
                command.TripId,
                request.ParcelId,
                request.IncidentId,
                null,
                "WHILE_PENDING_AND_CURRENT_TRIP_ASSIGNMENT",
                now,
                cancellationToken);
        }

        foreach (var request in departures)
        {
            await ParcelApprovalRequestedEvent.EnqueueAsync(
                _outbox,
                request.Id,
                "STOP_DEPARTURE",
                command.OperatorId,
                targetDriverUserId,
                command.TripId,
                null,
                null,
                request.StopId,
                "WHILE_PENDING_AND_UNRESOLVED_SNAPSHOT_MATCHES_BEFORE_STOP_DEPARTURE",
                now,
                cancellationToken);
        }

        return custody.Count + departures.Count;
    }
}
