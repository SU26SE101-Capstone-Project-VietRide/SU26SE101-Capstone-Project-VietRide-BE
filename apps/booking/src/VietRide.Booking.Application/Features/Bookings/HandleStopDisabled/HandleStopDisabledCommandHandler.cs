using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Application.Features.Bookings.HandleStopDisabled;

public sealed class HandleStopDisabledCommandHandler(
    IBookingRepository bookings,
    IBookingPendingActionRepository pendingActions,
    IBookingStatsRepository stats,
    IIntegrationEventOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock,
    ITripServiceClient tripClient) : IRequestHandler<HandleStopDisabledCommand, int>
{
    public async Task<int> Handle(HandleStopDisabledCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var affected = bookings.QueryNoTracking()
            .Where(x => x.OperatorId == request.OperatorId
                && (x.PickupStopId == request.StopId || x.DropoffStopId == request.StopId)
                && x.Status == BookingStatus.CONFIRMED)
            .Select(x => new
            {
                x.Id,
                x.TripId,
                x.PassengerUserId,
                x.TripCurrentDeparture,
                IsPickup = x.PickupStopId == request.StopId
            })
            .ToList();

        var planned = new List<PlannedAction>();
        foreach (var booking in affected)
        {
            var existingAction = pendingActions.Query().FirstOrDefault(x => x.BookingId == booking.Id && x.ResolvedAt == null);
            if (existingAction is not null && IsSameStop(existingAction.Metadata, request.StopId))
                continue;

            var snapshot = await tripClient.GetTripSnapshotAsync(booking.TripId, cancellationToken);
            if (snapshot is null || snapshot.Status is not ("SCHEDULED" or "BOARDING"))
                continue;

            var affectedField = booking.IsPickup ? "PICKUP" : "DROPOFF";
            var fallbackStationId = affectedField == "PICKUP" ? snapshot.OriginStation.Id : snapshot.DestinationStation.Id;

            var deadline = now.AddHours(24);
            if (booking.TripCurrentDeparture.HasValue)
                deadline = deadline < booking.TripCurrentDeparture.Value.AddHours(-2)
                    ? deadline : booking.TripCurrentDeparture.Value.AddHours(-2);

            planned.Add(new PlannedAction(
                booking.Id,
                booking.PassengerUserId,
                existingAction,
                deadline,
                JsonSerializer.Serialize(new
                {
                    disabledStopId = request.StopId,
                    affectedField,
                    suggestedStopId = request.ReplacedByStopId,
                    fallbackStationId,
                }, EventJsonOptions)));
        }

        if (!await stats.TryClaimProcessedEventAsync("trip.stop.disabled", request.EventId, now, cancellationToken))
            return 0;

        foreach (var action in planned)
        {
            if (action.ExistingAction is not null)
            {
                action.ExistingAction.Resolve(BookingPendingActionResolved.SUPERSEDED, now);
                pendingActions.Update(action.ExistingAction);
            }

            await pendingActions.AddAsync(BookingPendingAction.Create(
                action.BookingId, BookingPendingActionReason.STOP_DISABLED, action.Deadline,
                null, action.Metadata), cancellationToken);
        }

        var recipients = planned.Select(action => action.PassengerUserId).ToHashSet();
        var created = planned.Count;

        if (recipients.Count > 0)
        {
            var evt = new StopDisabledBookingAffectedIntegrationEvent(
                request.EventId, now, request.StopId, request.ReplacedByStopId,
                recipients, created);
            await outbox.EnqueueAsync(
                evt.EventId,
                evt.EventType,
                JsonSerializer.Serialize(evt, EventJsonOptions),
                cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return created;
    }

    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record PlannedAction(
        Guid BookingId,
        Guid PassengerUserId,
        BookingPendingAction? ExistingAction,
        DateTimeOffset Deadline,
        string Metadata);

    private static bool IsSameStop(string? metadata, Guid stopId)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return false;
        try
        {
            using var document = JsonDocument.Parse(metadata);
            return document.RootElement.TryGetProperty("disabledStopId", out var value)
                && value.TryGetGuid(out var stored) && stored == stopId;
        }
        catch (JsonException) { return false; }
    }
}
