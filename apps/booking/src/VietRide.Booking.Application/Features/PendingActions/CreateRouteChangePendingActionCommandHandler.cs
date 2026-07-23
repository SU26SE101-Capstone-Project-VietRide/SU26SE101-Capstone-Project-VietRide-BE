using System.Text.Json;
using MediatR;
using VietRide.Booking.Application.Abstractions.Jobs;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.UnitOfWork;

namespace VietRide.Booking.Application.Features.PendingActions;

public sealed class CreateRouteChangePendingActionCommandHandler(
    IBookingRepository bookings,
    IBookingPendingActionRepository pendingActions,
    IRouteChangeExpiryScheduler expiryScheduler,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateRouteChangePendingActionCommand, int>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<int> Handle(
        CreateRouteChangePendingActionCommand request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        var schedules = new Dictionary<Guid, DateTimeOffset>();
        var created = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await bookings.AcquireEventLockAsync(request.EventId, cancellationToken);
            await pendingActions.GetActiveByTripForUpdateAsync(
                request.TripId,
                request.OperatorId,
                cancellationToken);
            var confirmed = await bookings.GetConfirmedByTripAsync(
                request.TripId,
                request.OperatorId,
                cancellationToken);
            var confirmedById = confirmed.ToDictionary(booking => booking.Id);
            var count = 0;

            foreach (var affected in request.AffectedBookings.OrderBy(item => item.BookingId))
            {
                if (!confirmedById.TryGetValue(affected.BookingId, out var booking))
                {
                    continue;
                }

                var replay = await pendingActions.GetByBookingAndSourceEventAsync(
                    booking.Id,
                    request.EventId,
                    cancellationToken);
                var replayAction = replay.FirstOrDefault(action =>
                    action.Reason == BookingPendingActionReason.ROUTE_CHANGE);
                if (replayAction is not null)
                {
                    schedules.TryAdd(replayAction.Id, replayAction.Deadline.AddSeconds(1));
                    continue;
                }

                var active = await pendingActions.GetActiveByBookingIdAsync(
                    booking.Id,
                    cancellationToken);
                if (active is not null)
                {
                    active.Resolve(BookingPendingActionResolved.SUPERSEDED, request.OccurredAt);
                    pendingActions.Update(active);
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                }

                var deadline = CalculateDeadline(request.OccurredAt, request.TripStatus);
                var orderedCandidates = affected.CandidateStops
                    .OrderBy(candidate => candidate.Sequence)
                    .Select(candidate => new
                    {
                        candidate.StopId,
                        candidate.StationId,
                        candidate.StationName,
                        candidate.Sequence,
                        candidate.EstimatedArrivalAt,
                    })
                    .ToArray();
                var metadata = JsonSerializer.Serialize(new
                {
                    sourceEventId = request.EventId,
                    request.TripId,
                    request.OperatorId,
                    request.TripStatus,
                    request.AlternativeRouteId,
                    deadline,
                    candidateStops = orderedCandidates,
                }, JsonOptions);
                var action = BookingPendingAction.Create(
                    booking.Id,
                    BookingPendingActionReason.ROUTE_CHANGE,
                    deadline,
                    metadata: metadata);
                await pendingActions.AddAsync(action, cancellationToken);
                schedules.Add(action.Id, deadline.AddSeconds(1));
                count++;
            }

            return count;
        }, cancellationToken);

        foreach (var schedule in schedules.OrderBy(item => item.Key))
        {
            expiryScheduler.EnsureScheduled(schedule.Key, schedule.Value);
        }

        return created;
    }

    public static DateTimeOffset CalculateDeadline(DateTimeOffset occurredAt, string tripStatus)
        => NormalizeDeadline(tripStatus == "IN_PROGRESS"
            ? occurredAt.AddMinutes(30)
            : occurredAt.AddMinutes(60));

    private static DateTimeOffset NormalizeDeadline(DateTimeOffset deadline)
    {
        var utc = deadline.ToUniversalTime();
        return utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMicrosecond));
    }

    private static void Validate(CreateRouteChangePendingActionCommand request)
    {
        if (request.EventId == Guid.Empty
            || request.TripId == Guid.Empty
            || request.OperatorId == Guid.Empty
            || request.AlternativeRouteId == Guid.Empty
            || request.OccurredAt == default)
        {
            throw new ArgumentException("Route-change event contains an empty required value.");
        }

        if (request.TripStatus is not ("SCHEDULED" or "BOARDING" or "IN_PROGRESS"))
        {
            throw new ArgumentException("Route-change event contains an invalid Trip status.");
        }

        if (request.AffectedBookings.Select(item => item.BookingId).Distinct().Count()
            != request.AffectedBookings.Count)
        {
            throw new ArgumentException("Route-change event contains duplicate Booking ids.");
        }

        foreach (var affected in request.AffectedBookings)
        {
            if (affected.BookingId == Guid.Empty || affected.CandidateStops.Count == 0)
            {
                throw new ArgumentException("Route-change affected Booking is invalid.");
            }

            var sequences = new HashSet<int>();
            var identities = new HashSet<(Guid? StopId, Guid? StationId)>();
            foreach (var candidate in affected.CandidateStops)
            {
                if (candidate.StopId.HasValue == candidate.StationId.HasValue
                    || candidate.StationName.Trim().Length == 0
                    || candidate.Sequence <= 0
                    || !sequences.Add(candidate.Sequence)
                    || !identities.Add((candidate.StopId, candidate.StationId))
                    || candidate.EstimatedArrivalAt == default)
                {
                    throw new ArgumentException("Route-change candidate stop is invalid.");
                }
            }
        }
    }
}
