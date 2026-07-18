using System.Text.Json;
using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
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
    IClock clock) : IRequestHandler<HandleStopDisabledCommand, int>
{
    public async Task<int> Handle(HandleStopDisabledCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (!await stats.TryClaimProcessedEventAsync("trip.stop.disabled", request.EventId, now, cancellationToken))
            return 0;

        var affected = bookings.QueryNoTracking()
            .Where(x => x.OperatorId == request.OperatorId
                && (x.PickupStopId == request.StopId || x.DropoffStopId == request.StopId)
                && (x.Status == BookingStatus.PENDING_PAYMENT || x.Status == BookingStatus.CONFIRMED))
            .Select(x => new { x.Id, x.PassengerUserId, x.TripCurrentDeparture })
            .ToList();

        var recipients = new HashSet<Guid>();
        var created = 0;
        foreach (var booking in affected)
        {
            recipients.Add(booking.PassengerUserId);
            var existingAction = pendingActions.Query().FirstOrDefault(x => x.BookingId == booking.Id && x.ResolvedAt == null);
            if (existingAction is not null)
            {
                existingAction.Resolve(BookingPendingActionResolved.SUPERSEDED, now);
                pendingActions.Update(existingAction);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            var deadline = now.AddHours(24);
            if (booking.TripCurrentDeparture.HasValue)
                deadline = deadline < booking.TripCurrentDeparture.Value.AddHours(-2)
                    ? deadline : booking.TripCurrentDeparture.Value.AddHours(-2);
            if (deadline < now) deadline = now;

            await pendingActions.AddAsync(BookingPendingAction.Create(
                booking.Id, BookingPendingActionReason.STOP_DISABLED, deadline,
                BookingPendingActionSeverity.MAJOR,
                JsonSerializer.Serialize(new { request.StopId, request.ReplacedByStopId })), cancellationToken);
            created++;
        }

        if (recipients.Count > 0)
        {
            await outbox.EnqueueAsync("booking.stop_disabled.affected", JsonSerializer.Serialize(new
            {
                request.StopId,
                request.ReplacedByStopId,
                recipientUserIds = recipients,
                affectedBookingCount = affected.Count,
                occurredAt = now,
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)), cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return created;
    }
}
