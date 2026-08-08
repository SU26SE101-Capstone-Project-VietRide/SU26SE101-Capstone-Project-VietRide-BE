using System.Text.Json;
using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Domain.Constants;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.Services;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Application.Features.Bookings.ResolvePendingAction;

public sealed class ResolvePendingActionCommandHandler(
    IBookingPendingActionRepository pendingActions,
    IBookingRepository bookings,
    IBookingStatusHistoryRepository statusHistory,
    IIntegrationEventOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock,
    IBookingStationCanonicalizer? stationCanonicalizer = null)
    : IRequestHandler<ResolvePendingActionCommand, ResolvePendingActionResult>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<ResolvePendingActionResult> Handle(
        ResolvePendingActionCommand request,
        CancellationToken cancellationToken)
        => unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var now = clock.UtcNow;

            // All resolution paths lock action first and Booking second to avoid lock-order inversions.
            var pendingAction = await pendingActions.GetByIdForUpdateAsync(request.ActionId, cancellationToken);
            var booking = await bookings.FindByIdForUpdateAsync(request.BookingId, cancellationToken);

            if (booking is null || booking.PassengerUserId != request.PassengerUserId)
            {
                throw BookingNotFound();
            }

            if (pendingAction is null)
            {
                throw new CodedNotFoundException(
                    "BOOKING_PENDING_ACTION_NOT_FOUND",
                    "Booking pending action was not found.");
            }

            if (pendingAction.BookingId != booking.Id)
            {
                throw BookingNotFound();
            }

            ThrowIfTerminal(pendingAction);

            if (booking.Status != BookingStatus.CONFIRMED)
            {
                throw NotResolvable();
            }

            var resolvedAction = Enum.Parse<BookingPendingActionResolved>(request.Action!, ignoreCase: false);
            if (pendingAction.Reason == BookingPendingActionReason.ROUTE_CHANGE)
            {
                await ResolveRouteChangeAsync(
                    request,
                    pendingAction,
                    booking,
                    resolvedAction,
                    now,
                    cancellationToken);
            }
            else if (pendingAction.Reason == BookingPendingActionReason.SCHEDULE_CHANGE
                && pendingAction.Severity is not null)
            {
                if (request.SelectedStopId.HasValue || request.SelectedStationId.HasValue)
                {
                    throw InvalidSelection("Schedule-change actions do not accept a selected candidate.");
                }

                var frozen = ParseFrozenMetadata(pendingAction, booking.TotalAmount);
                var effectiveCutoff = GetEffectiveCutoff(
                    pendingAction,
                    frozen.InitialDeadline,
                    frozen.TerminalDeadline);
                if (now > effectiveCutoff)
                {
                    throw Expired();
                }

                pendingAction.ResolveScheduleChange(resolvedAction, now, effectiveCutoff);
                if (resolvedAction == BookingPendingActionResolved.REJECTED)
                {
                    await CancelAndPublishAsync(
                        booking,
                        BookingCancellationReason.SCHEDULE_CHANGED,
                        frozen.RefundAmount.Amount,
                        now,
                        cancellationToken);
                }
            }
            else
            {
                throw NotResolvable();
            }

            pendingActions.Update(pendingAction);

            return new ResolvePendingActionResult(
                booking.Id,
                pendingAction.Id,
                resolvedAction.ToString(),
                now);
        }, cancellationToken);

    private async Task ResolveRouteChangeAsync(
        ResolvePendingActionCommand request,
        BookingPendingAction pendingAction,
        VietRide.Booking.Domain.Entities.Booking booking,
        BookingPendingActionResolved resolvedAction,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (now > pendingAction.Deadline)
        {
            throw Expired();
        }

        var candidates = ParseRouteCandidates(pendingAction);
        if (resolvedAction == BookingPendingActionResolved.ACCEPTED)
        {
            if (request.SelectedStopId.HasValue == request.SelectedStationId.HasValue)
            {
                throw InvalidSelection("ACCEPTED requires exactly one selected candidate identity.");
            }

            var selectedStationId = request.SelectedStationId;
            StationCanonicalizationResult? canonicalization = null;
            if (selectedStationId.HasValue)
            {
                if (stationCanonicalizer is null)
                {
                    throw new InvalidOperationException(
                        "Booking Station canonicalization is required for a route-change station selection.");
                }

                canonicalization = await stationCanonicalizer.LockAndResolveAsync(
                    BookingStationCanonicalization.Collect(
                        [selectedStationId, .. candidates.Select(candidate => candidate.StationId)]),
                    cancellationToken);
                selectedStationId = canonicalization.Resolve(selectedStationId);
            }

            var matched = candidates.Count(candidate =>
                candidate.StopId == request.SelectedStopId
                && (canonicalization?.Resolve(candidate.StationId) ?? candidate.StationId)
                    == selectedStationId);
            if (matched != 1)
            {
                throw InvalidSelection("Selected route-change candidate is not present in frozen metadata.");
            }

            booking.ChangePickup(selectedStationId, request.SelectedStopId);
            bookings.Update(booking);
        }
        else
        {
            if (request.SelectedStopId.HasValue || request.SelectedStationId.HasValue)
            {
                throw InvalidSelection("REJECTED does not accept a selected candidate.");
            }
        }

        pendingAction.ResolveRouteChange(resolvedAction, now);
        if (resolvedAction == BookingPendingActionResolved.REJECTED)
        {
            await CancelAndPublishAsync(
                booking,
                BookingCancellationReason.ROUTE_CHANGED_REFUSED,
                booking.TotalAmount.Amount,
                now,
                cancellationToken);
        }
    }

    private async Task CancelAndPublishAsync(
        VietRide.Booking.Domain.Entities.Booking booking,
        BookingCancellationReason reason,
        long refundAmount,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        booking.Cancel(reason, now, refundOverride: true);
        bookings.Update(booking);
        await statusHistory.AddAsync(
            BookingStatusHistory.Create(
                booking.Id,
                BookingStatus.CANCELLED,
                now,
                BookingStatusHistorySource.CancelBooking,
                actorUserId: null,
                reason.ToString()),
            cancellationToken);

        var eventId = Guid.NewGuid();
        var cancelled = new BookingCancelledIntegrationEvent(
            eventId,
            now,
            booking.Id,
            booking.BookingCode.Value,
            booking.PassengerUserId,
            refundAmount,
            true,
            reason.ToString(),
            booking.Tickets.Select(ticket => ticket.TicketCode.Value).Order(StringComparer.Ordinal).ToArray(),
            booking.Tickets.Count,
            booking.TripId,
            BookingStatus.CONFIRMED.ToString(),
            booking.Passengers.Select(passenger => passenger.SeatNumber).OfType<string>().Order(StringComparer.Ordinal).ToArray());
        await outbox.EnqueueAsync(
            eventId,
            BookingCancelledIntegrationEvent.EventTypeValue,
            JsonSerializer.Serialize(cancelled, JsonOptions),
            cancellationToken);
    }

    private static IReadOnlyList<(Guid? StopId, Guid? StationId)> ParseRouteCandidates(
        BookingPendingAction action)
    {
        try
        {
            using var document = JsonDocument.Parse(action.Metadata ?? string.Empty);
            var root = document.RootElement;
            var expected = new HashSet<string>(StringComparer.Ordinal)
            {
                "sourceEventId", "tripId", "operatorId", "tripStatus", "alternativeRouteId",
                "deadline", "originalStopId", "fallbackDestinationStationId", "shuttleRequired",
                "candidateStops",
            };
            if (root.ValueKind != JsonValueKind.Object
                || !root.EnumerateObject().Select(property => property.Name)
                    .ToHashSet(StringComparer.Ordinal).SetEquals(expected)
                || root.GetProperty("deadline").GetDateTimeOffset() != action.Deadline
                || root.GetProperty("originalStopId").GetGuid() == Guid.Empty
                || root.GetProperty("fallbackDestinationStationId").GetGuid() == Guid.Empty
                || !root.GetProperty("shuttleRequired").GetBoolean())
            {
                throw new InvalidOperationException();
            }

            var candidates = new List<(Guid?, Guid?)>();
            var previousSequence = 0;
            foreach (var candidate in root.GetProperty("candidateStops").EnumerateArray())
            {
                var fields = candidate.EnumerateObject().Select(property => property.Name)
                    .ToHashSet(StringComparer.Ordinal);
                if (!fields.SetEquals(
                    ["stopId", "stationId", "stationName", "sequence", "estimatedArrivalAt"]))
                {
                    throw new InvalidOperationException();
                }

                var stop = ReadNullableGuid(candidate.GetProperty("stopId"));
                var station = ReadNullableGuid(candidate.GetProperty("stationId"));
                var sequence = candidate.GetProperty("sequence").GetInt32();
                if (stop.HasValue == station.HasValue
                    || sequence <= previousSequence
                    || string.IsNullOrWhiteSpace(candidate.GetProperty("stationName").GetString())
                    || candidate.GetProperty("estimatedArrivalAt").GetDateTimeOffset() == default)
                {
                    throw new InvalidOperationException();
                }

                previousSequence = sequence;
                candidates.Add((stop, station));
            }

            return candidates.Count > 0 ? candidates : throw new InvalidOperationException();
        }
        catch (Exception exception) when (exception is JsonException
            or InvalidOperationException
            or FormatException)
        {
            throw NotResolvable();
        }
    }

    private static Guid? ReadNullableGuid(JsonElement value)
        => value.ValueKind == JsonValueKind.Null ? null : value.GetGuid();

    private static DateTimeOffset GetEffectiveCutoff(
        BookingPendingAction action,
        DateTimeOffset initialDeadline,
        DateTimeOffset? terminalDeadline)
    {
        try
        {
            return ScheduleChangeResolutionStateMachine.GetEffectiveCutoff(
                action,
                initialDeadline,
                terminalDeadline);
        }
        catch (InvalidOperationException)
        {
            throw NotResolvable();
        }
    }

    private static (DateTimeOffset InitialDeadline, DateTimeOffset? TerminalDeadline, Money RefundAmount) ParseFrozenMetadata(
        BookingPendingAction action,
        Money bookingTotal)
    {
        try
        {
            using var document = JsonDocument.Parse(action.Metadata ?? string.Empty);
            var root = document.RootElement;
            var expected = new HashSet<string>(StringComparer.Ordinal)
            {
                "sourceEventId", "oldDeparture", "newDeparture", "severity", "initialDeadline",
                "terminalDeadline", "refundBasisAmount", "refundPercent", "refundAmount",
            };
            if (root.ValueKind != JsonValueKind.Object
                || !root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal).SetEquals(expected)
                || root.GetProperty("sourceEventId").GetGuid() == Guid.Empty
                || root.GetProperty("oldDeparture").GetDateTimeOffset() == default
                || root.GetProperty("newDeparture").GetDateTimeOffset() == default)
            {
                throw new InvalidOperationException();
            }

            var severity = root.GetProperty("severity").GetString();
            var initialDeadline = root.GetProperty("initialDeadline").GetDateTimeOffset();
            var terminalElement = root.GetProperty("terminalDeadline");
            var terminalDeadline = terminalElement.ValueKind == JsonValueKind.Null
                ? (DateTimeOffset?)null
                : terminalElement.GetDateTimeOffset();
            var refundBasisAmount = root.GetProperty("refundBasisAmount").GetInt64();
            var refundPercent = root.GetProperty("refundPercent").GetInt32();
            var storedRefundAmount = root.GetProperty("refundAmount").GetInt64();
            var expectedPercent = action.Severity == BookingPendingActionSeverity.MEDIUM ? 50 : 100;
            var calculated = CancellationRefundCalculator.CalculateExplicitPercentRefund(
                Money.FromRaw(refundBasisAmount),
                refundPercent);

            if (!string.Equals(severity, action.Severity.ToString(), StringComparison.Ordinal)
                || refundBasisAmount != bookingTotal.Amount
                || refundPercent != expectedPercent
                || storedRefundAmount != calculated.Amount)
            {
                throw new InvalidOperationException();
            }

            return (
                initialDeadline,
                terminalDeadline,
                calculated);
        }
        catch (Exception exception) when (exception is JsonException
            or InvalidOperationException
            or FormatException
            or ArgumentOutOfRangeException)
        {
            throw NotResolvable();
        }
    }

    private static void ThrowIfTerminal(BookingPendingAction action)
    {
        if (action.ResolvedAction == BookingPendingActionResolved.SUPERSEDED)
        {
            throw new CodedConflictException(
                "BOOKING_PENDING_ACTION_SUPERSEDED",
                "Booking pending action was superseded.");
        }

        if (action.ResolvedAt.HasValue || action.ResolvedAction.HasValue)
        {
            throw new CodedConflictException(
                "BOOKING_PENDING_ACTION_ALREADY_RESOLVED",
                "Booking pending action was already resolved.");
        }
    }

    private static CodedNotFoundException BookingNotFound()
        => new("BOOKING_NOT_FOUND", "Booking not found.");

    private static CodedConflictException NotResolvable()
        => new(
            "BOOKING_PENDING_ACTION_NOT_RESOLVABLE",
            "Booking pending action cannot be resolved by this endpoint.");

    private static CodedConflictException Expired()
        => new(
            "BOOKING_PENDING_ACTION_EXPIRED",
            "Booking pending action has expired.");

    private static CodedValidationException InvalidSelection(string message)
        => new(
            "VALIDATION_ERROR",
            message,
            [new ValidationError("selectedStopId", message)]);

}
