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

namespace VietRide.Booking.Application.Features.Bookings.HandleVehicleSwap;

public sealed class HandleVehicleSwapCommandHandler(
    IBookingRepository bookings,
    IBookingPendingActionRepository pendingActions,
    IIntegrationEventOutbox outbox,
    IUnitOfWork unitOfWork,
    IPendingActionRealertScheduler scheduler,
    IClock clock) : IRequestHandler<HandleVehicleSwapCommand, int>
{
    private const string SeatRemoved = "SEAT_REMOVED";
    private const string SeatDisabled = "SEAT_DISABLED";
    private const string SeatTypeDowngraded = "SEAT_TYPE_DOWNGRADED";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<int> Handle(HandleVehicleSwapCommand request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var now = clock.UtcNow;
        var deadline = Min(request.OccurredAt.AddHours(4), request.DepartureDateTime.AddMinutes(-30));
        if (deadline <= now)
        {
            return 0;
        }

        var scheduleDueAt = request.OccurredAt.AddHours(2);
        var schedules = new Dictionary<Guid, DateTimeOffset>();
        var activeByBooking = new Dictionary<Guid, BookingPendingAction?>();
        var existingByBooking = new Dictionary<Guid, IReadOnlyList<BookingPendingAction>>();
        var seenImpacts = new HashSet<string>(StringComparer.Ordinal);
        var created = 0;

        foreach (var impact in NormalizeImpacts(request.SeatImpacts))
        {
            var impactKey = BuildImpactKey(impact);
            if (!seenImpacts.Add(impactKey))
            {
                continue;
            }

            var booking = bookings.QueryNoTracking()
                .FirstOrDefault(candidate => candidate.Id == impact.BookingId
                    && candidate.TripId == request.TripId
                    && candidate.OperatorId == request.OperatorId
                    && candidate.Status == BookingStatus.CONFIRMED);
            if (booking is null)
            {
                continue;
            }

            if (!existingByBooking.TryGetValue(booking.Id, out var existingActions))
            {
                existingActions = await pendingActions.GetByBookingAndSourceEventAsync(
                    booking.Id,
                    request.EventId,
                    cancellationToken);
                existingByBooking.Add(booking.Id, existingActions);
            }

            var existingAction = existingActions.FirstOrDefault(action => MetadataMatches(action.Metadata, impact));
            if (existingAction is not null)
            {
                schedules.TryAdd(existingAction.Id, scheduleDueAt);
                continue;
            }

            var activeLoadedFromRepository = false;
            if (!activeByBooking.TryGetValue(booking.Id, out var activeAction))
            {
                activeAction = await pendingActions.GetActiveByBookingIdAsync(booking.Id, cancellationToken);
                activeLoadedFromRepository = true;
            }

            activeAction?.Resolve(BookingPendingActionResolved.SUPERSEDED, now);
            if (activeAction is not null && activeLoadedFromRepository)
            {
                pendingActions.Update(activeAction);
            }

            var metadata = JsonSerializer.Serialize(new
            {
                sourceEventId = request.EventId,
                seatNumbers = impact.SeatNumbers,
                reason = impact.Reason,
            }, JsonOptions);
            var action = BookingPendingAction.Create(
                booking.Id,
                BookingPendingActionReason.PENDING_SEAT_ASSIGNMENT,
                deadline,
                severity: null,
                metadata);
            await pendingActions.AddAsync(action, cancellationToken);
            activeByBooking[booking.Id] = action;

            var integrationEvent = new BookingSeatReassignmentRequiredIntegrationEvent(
                Guid.NewGuid(),
                now,
                booking.Id,
                booking.TripId,
                booking.PassengerUserId,
                action.Id,
                deadline,
                impact.SeatNumbers,
                impact.Reason);
            await outbox.EnqueueAsync(
                BookingSeatReassignmentRequiredIntegrationEvent.EventTypeValue,
                JsonSerializer.Serialize(integrationEvent, JsonOptions),
                cancellationToken);

            schedules.Add(action.Id, scheduleDueAt);
            created++;
        }

        if (created > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        foreach (var schedule in schedules.OrderBy(item => item.Key))
        {
            scheduler.EnsureScheduled(schedule.Key, schedule.Value);
        }

        return created;
    }

    private static IReadOnlyList<VehicleSwapSeatImpact> NormalizeImpacts(
        IReadOnlyCollection<VehicleSwapSeatImpact> impacts)
        => impacts
            .Select(impact => new VehicleSwapSeatImpact(
                impact.BookingId,
                impact.SeatNumbers
                    .Select(NormalizeSeatNumber)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                impact.Reason))
            .OrderBy(impact => impact.BookingId)
            .ThenBy(impact => impact.Reason, StringComparer.Ordinal)
            .ThenBy(impact => string.Join('\u001f', impact.SeatNumbers), StringComparer.Ordinal)
            .ToArray();

    private static void ValidateRequest(HandleVehicleSwapCommand request)
    {
        if (request.EventId == Guid.Empty || request.TripId == Guid.Empty || request.OperatorId == Guid.Empty)
        {
            throw new ArgumentException("Vehicle-swap event, Trip, and Operator ids must be non-empty.");
        }

        if (request.OccurredAt == default || request.DepartureDateTime == default)
        {
            throw new ArgumentException("Vehicle-swap timestamps must be valid.");
        }

        ArgumentNullException.ThrowIfNull(request.SeatImpacts);
        foreach (var impact in request.SeatImpacts)
        {
            if (impact.BookingId == Guid.Empty || impact.SeatNumbers is null || impact.SeatNumbers.Count == 0)
            {
                throw new ArgumentException("Each seat impact requires a Booking and at least one seat.");
            }

            if (impact.Reason is not SeatRemoved and not SeatDisabled and not SeatTypeDowngraded)
            {
                throw new ArgumentException("Vehicle-swap seat impact reason is not registered.");
            }
        }
    }

    private static string NormalizeSeatNumber(string seatNumber)
    {
        var normalized = seatNumber?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > 20)
        {
            throw new ArgumentException("Seat number must contain 1 to 20 characters.", nameof(seatNumber));
        }

        return normalized;
    }

    private static string BuildImpactKey(VehicleSwapSeatImpact impact)
        => $"{impact.BookingId:N}:{impact.Reason}:{string.Join(',', impact.SeatNumbers)}";

    private static bool MetadataMatches(string? metadata, VehicleSwapSeatImpact impact)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(metadata);
            var root = document.RootElement;
            if (!root.TryGetProperty("reason", out var reason)
                || !string.Equals(reason.GetString(), impact.Reason, StringComparison.Ordinal)
                || !root.TryGetProperty("seatNumbers", out var seats)
                || seats.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var storedSeats = seats.EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty)
                .Order(StringComparer.Ordinal)
                .ToArray();
            return storedSeats.SequenceEqual(impact.SeatNumbers, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second)
        => first <= second ? first : second;
}
