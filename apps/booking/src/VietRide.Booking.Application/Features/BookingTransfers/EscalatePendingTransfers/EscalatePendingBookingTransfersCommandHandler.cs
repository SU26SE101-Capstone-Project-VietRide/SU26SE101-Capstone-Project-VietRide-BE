using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Events;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Application.Features.BookingTransfers.EscalatePendingTransfers;

public sealed class EscalatePendingBookingTransfersCommandHandler(
    IBookingTransferRepository transfers,
    IBookingRepository bookings,
    IIntegrationEventOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<EscalatePendingBookingTransfersCommand, int>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan ConfirmationWindow = TimeSpan.FromHours(2);
    private const int MaxGroups = 200;

    public async Task<int> Handle(
        EscalatePendingBookingTransfersCommand request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.ToUniversalTime();
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var candidates = await transfers.AcquirePendingEscalationBatchAsync(
                now.Subtract(ConfirmationWindow),
                MaxGroups,
                cancellationToken);
            if (candidates.Count == 0)
            {
                await unitOfWork.CommitAsync(cancellationToken);
                return 0;
            }

            var bookingIds = candidates.Select(transfer => transfer.BookingId).Distinct().ToArray();
            var bookingById = await bookings.QueryNoTracking()
                .Where(booking => bookingIds.Contains(booking.Id))
                .ToDictionaryAsync(booking => booking.Id, cancellationToken);

            var escalatedGroups = 0;
            foreach (var group in candidates
                .GroupBy(transfer => new { transfer.BookingId, transfer.NewTripId })
                .OrderBy(group => group.Key.BookingId)
                .ThenBy(group => group.Key.NewTripId))
            {
                if (!bookingById.TryGetValue(group.Key.BookingId, out var booking))
                    continue;

                var changed = group.Where(transfer => transfer.Escalate()).ToArray();
                if (changed.Length == 0)
                    continue;

                foreach (var transfer in changed)
                    transfers.Update(transfer);

                var integrationEvent = new BookingTransferEscalatedIntegrationEvent(
                    Guid.NewGuid(),
                    now,
                    booking.Id,
                    booking.BookingCode.Value,
                    booking.OperatorId,
                    changed[0].OriginalTripId,
                    group.Key.NewTripId,
                    changed.Select(transfer => transfer.Id).OrderBy(id => id).ToArray(),
                    changed.Min(transfer => transfer.TransferredAt));
                await outbox.EnqueueAsync(
                    integrationEvent.EventId,
                    BookingTransferEscalatedIntegrationEvent.EventTypeValue,
                    JsonSerializer.Serialize(integrationEvent, JsonOptions),
                    cancellationToken);
                escalatedGroups++;
            }

            if (escalatedGroups > 0)
                await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return escalatedGroups;
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
