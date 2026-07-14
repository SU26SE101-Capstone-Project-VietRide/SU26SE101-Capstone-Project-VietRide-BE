using System.Text.Json;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Stops;

public sealed class DisableStopHandler : IRequestHandler<DisableStopCommand, DisableStopResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IStopRepository stops;
    private readonly IIdentityInternalClient identity;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IClock clock;
    private readonly IBookingImpactClient bookingImpact;

    public DisableStopHandler(IStopRepository stops, IIdentityInternalClient identity,
        IIntegrationEventOutbox outbox, IClock clock, IBookingImpactClient bookingImpact)
    {
        this.stops = stops;
        this.identity = identity;
        this.outbox = outbox;
        this.clock = clock;
        this.bookingImpact = bookingImpact;
    }

    public async Task<DisableStopResponse> Handle(DisableStopCommand request, CancellationToken cancellationToken)
    {
        var stop = stops.Query().FirstOrDefault(x => x.Id == request.StopId && x.DeletedAt == null)
            ?? throw new CodedNotFoundException("STOP_NOT_FOUND", "Stop was not found.");
        if (request.OperatorId.HasValue)
        {
            await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(identity, request.OperatorId.Value, cancellationToken);
            if (stop.OperatorId != request.OperatorId.Value)
                throw new CodedNotFoundException("STOP_NOT_FOUND", "Stop was not found.");
        }

        if (request.ReplacedByStopId.HasValue)
        {
            ValidateReplacement(stop, request.ReplacedByStopId.Value);
        }

        var now = clock.UtcNow;
        var activeBookingCount = await bookingImpact.GetActiveBookingCountByStopAsync(
            stop.Id, stop.OperatorId, cancellationToken);
        // TransactionBehavior owns the command transaction and SaveChanges boundary.
        // Keeping an explicit transaction here would nest EF transactions at runtime.
        stop.SoftDelete(now, request.ReplacedByStopId);
        stops.Update(stop);
        await outbox.EnqueueAsync("trip.stop.disabled", JsonSerializer.Serialize(new
        {
            stopId = stop.Id,
            operatorId = stop.OperatorId,
            replacedByStopId = request.ReplacedByStopId,
            occurredAt = now,
        }, JsonOptions), cancellationToken);

        return new DisableStopResponse(StopMapper.ToDto(stop), "STOP_DISABLED_BOOKING_AFFECTED", activeBookingCount);
    }

    private void ValidateReplacement(Domain.Entities.Stop source, Guid replacementId)
    {
        var replacement = stops.QueryNoTracking().FirstOrDefault(x => x.Id == replacementId && x.DeletedAt == null);
        if (replacement is null || !replacement.IsActive || replacement.OperatorId != source.OperatorId)
            throw new CodedValidationException("STOP_REPLACEMENT_INVALID", "Replacement stop must be active and belong to the same operator.");

        var visited = new HashSet<Guid> { source.Id };
        var cursor = replacement;
        while (true)
        {
            if (!visited.Add(cursor.Id))
                throw new CodedValidationException("STOP_REPLACEMENT_CYCLE", "Replacement stop would create a cycle.");
            if (!cursor.ReplacedByStopId.HasValue) break;
            cursor = stops.QueryNoTracking().FirstOrDefault(x => x.Id == cursor.ReplacedByStopId.Value)
                ?? throw new CodedValidationException("STOP_REPLACEMENT_INVALID", "Replacement chain is invalid.");
        }
    }
}
