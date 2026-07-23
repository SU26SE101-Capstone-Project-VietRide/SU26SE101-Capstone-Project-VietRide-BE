using System.Text.Json;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Events;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Trips;

public sealed class CancelTripCommandHandler : IRequestHandler<CancelTripCommand, CancelTripResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ITripRepository trips;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IUnitOfWork unitOfWork;
    private readonly IClock clock;

    public CancelTripCommandHandler(
        ITripRepository trips,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        this.trips = trips;
        this.outbox = outbox;
        this.unitOfWork = unitOfWork;
        this.clock = clock;
    }

    public async Task<CancelTripResponse> Handle(
        CancelTripCommand request,
        CancellationToken cancellationToken)
    {
        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var trip = await trips.GetForUpdateAsync(request.TripId, cancellationToken);
            if (trip is null || trip.OperatorId != request.OperatorId)
                throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");

            CancelTripPreviewQueryHandler.EnsureEditable(trip.Status);
            var now = clock.UtcNow;
            trip.Cancel(now, request.ActorUserId, request.Reason);

            var evt = new TripCancelledByOperatorIntegrationEvent(
                trip.Id,
                trip.OperatorId,
                now,
                request.Reason);
            await outbox.EnqueueAsync(
                evt.EventId,
                evt.EventType,
                JsonSerializer.Serialize(evt, JsonOptions),
                cancellationToken);
            return new CancelTripResponse(trip.Id, trip.Status.ToString());
        }, cancellationToken);
    }
}
