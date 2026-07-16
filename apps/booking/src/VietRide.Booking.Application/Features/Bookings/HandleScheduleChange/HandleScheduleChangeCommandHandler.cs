using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using VietRide.Booking.Application.Abstractions.Jobs;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Application.Features.Bookings.HandleScheduleChange;

public sealed class HandleScheduleChangeCommandHandler(
    IBookingRepository bookings,
    IBookingPendingActionRepository pendingActions,
    IIntegrationEventOutbox outbox,
    IUnitOfWork unitOfWork,
    IPendingActionRealertScheduler scheduler,
    IClock clock) : IRequestHandler<HandleScheduleChangeCommand, int>
{
    private static readonly TimeSpan IctOffset = TimeSpan.FromHours(7);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<int> Handle(
        HandleScheduleChangeCommand request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        var schedules = new Dictionary<Guid, DateTimeOffset>();
        var affected = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await bookings.AcquireEventLockAsync(request.EventId, cancellationToken);
            var confirmed = await bookings.GetConfirmedByTripAsync(
                request.TripId,
                request.OperatorId,
                cancellationToken);
            var changed = 0;

            foreach (var booking in confirmed)
            {
                var outgoingEventId = DeriveEventId(request.EventId, booking.Id, request.Severity);
                if (request.Severity == "MINOR")
                {
                    if (await bookings.HasOutboxEventAsync(
                            BookingScheduleChangeInformationalIntegrationEvent.EventTypeValue,
                            outgoingEventId,
                            cancellationToken))
                    {
                        continue;
                    }

                    var informational = new BookingScheduleChangeInformationalIntegrationEvent(
                        outgoingEventId,
                        request.OccurredAt,
                        booking.Id,
                        booking.TripId,
                        booking.PassengerUserId,
                        request.OldDeparture,
                        request.NewDeparture,
                        request.Severity);
                    await outbox.EnqueueAsync(
                        BookingScheduleChangeInformationalIntegrationEvent.EventTypeValue,
                        JsonSerializer.Serialize(informational, JsonOptions),
                        cancellationToken);
                    changed++;
                    continue;
                }

                var sourceActions = await pendingActions.GetByBookingAndSourceEventAsync(
                    booking.Id,
                    request.EventId,
                    cancellationToken);
                var existing = sourceActions.FirstOrDefault(action =>
                    action.Reason == BookingPendingActionReason.SCHEDULE_CHANGE);
                if (existing is not null)
                {
                    schedules.TryAdd(existing.Id, request.OccurredAt.AddHours(2));
                    continue;
                }

                var now = clock.UtcNow;
                var active = await pendingActions.GetActiveByBookingIdAsync(booking.Id, cancellationToken);
                if (active is not null)
                {
                    active.Resolve(BookingPendingActionResolved.SUPERSEDED, now);
                    pendingActions.Update(active);
                }

                var deadline = CalculateDeadline(request.OccurredAt, request.NewDeparture);
                var metadata = JsonSerializer.Serialize(new
                {
                    sourceEventId = request.EventId,
                    oldDeparture = request.OldDeparture,
                    newDeparture = request.NewDeparture,
                    severity = request.Severity,
                }, JsonOptions);
                var action = BookingPendingAction.Create(
                    booking.Id,
                    BookingPendingActionReason.SCHEDULE_CHANGE,
                    deadline,
                    Enum.Parse<BookingPendingActionSeverity>(request.Severity),
                    metadata);
                await pendingActions.AddAsync(action, cancellationToken);

                var required = new BookingScheduleChangeRequiredIntegrationEvent(
                    outgoingEventId,
                    request.OccurredAt,
                    booking.Id,
                    booking.TripId,
                    booking.PassengerUserId,
                    action.Id,
                    deadline,
                    request.OldDeparture,
                    request.NewDeparture,
                    request.Severity);
                await outbox.EnqueueAsync(
                    BookingScheduleChangeRequiredIntegrationEvent.EventTypeValue,
                    JsonSerializer.Serialize(required, JsonOptions),
                    cancellationToken);
                schedules.Add(action.Id, request.OccurredAt.AddHours(2));
                changed++;
            }

            return changed;
        }, cancellationToken);

        foreach (var schedule in schedules.OrderBy(item => item.Key))
        {
            scheduler.EnsureScheduled(schedule.Key, schedule.Value);
        }

        return affected;
    }

    public static DateTimeOffset CalculateDeadline(
        DateTimeOffset notifiedAt,
        DateTimeOffset newDeparture)
    {
        if (newDeparture - notifiedAt > TimeSpan.FromHours(24))
        {
            return Min(notifiedAt.AddHours(24), newDeparture.AddHours(-2));
        }

        return Max(notifiedAt.AddHours(1), newDeparture.AddMinutes(-30));
    }

    private static void Validate(HandleScheduleChangeCommand request)
    {
        if (request.EventId == Guid.Empty || request.TripId == Guid.Empty || request.OperatorId == Guid.Empty)
        {
            throw new ArgumentException("Schedule-change event, Trip, and Operator ids must be non-empty.");
        }

        if (request.OccurredAt == default
            || request.OldDeparture == default
            || request.NewDeparture == default
            || request.OldDeparture == request.NewDeparture)
        {
            throw new ArgumentException("Schedule-change timestamps are invalid.");
        }

        var expectedSeverity = CalculateSeverity(request.OldDeparture, request.NewDeparture);
        if (!string.Equals(request.Severity, expectedSeverity, StringComparison.Ordinal))
        {
            throw new ArgumentException("Schedule-change severity does not match the departure delta.");
        }
    }

    private static string CalculateSeverity(DateTimeOffset oldDeparture, DateTimeOffset newDeparture)
    {
        var delta = (newDeparture - oldDeparture).Duration();
        var sameLocalDate = oldDeparture.ToOffset(IctOffset).Date == newDeparture.ToOffset(IctOffset).Date;
        if (sameLocalDate && delta <= TimeSpan.FromHours(2))
        {
            return "MINOR";
        }

        return sameLocalDate && delta < TimeSpan.FromHours(6) ? "MEDIUM" : "MAJOR";
    }

    private static Guid DeriveEventId(Guid sourceEventId, Guid bookingId, string severity)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"booking.schedule-change:{sourceEventId:N}:{bookingId:N}:{severity}"));
        var bytes = hash[..16];
        bytes[7] = (byte)((bytes[7] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second)
        => first <= second ? first : second;

    private static DateTimeOffset Max(DateTimeOffset first, DateTimeOffset second)
        => first >= second ? first : second;
}
