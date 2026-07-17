using System.Text.Json;
using VietRide.Shared.Application.Outbox;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Events;
using VietRide.Trip.Application.Features.Vehicles;
using VietRide.Trip.Domain.Constants;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Services;

public sealed class TripVehicleSwapService : ITripVehicleSwapService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITripSeatRepository tripSeats;
    private readonly ITripAuditLogRepository auditLogs;
    private readonly IIntegrationEventOutbox outbox;

    public TripVehicleSwapService(
        ITripSeatRepository tripSeats,
        ITripAuditLogRepository auditLogs,
        IIntegrationEventOutbox outbox)
    {
        this.tripSeats = tripSeats;
        this.auditLogs = auditLogs;
        this.outbox = outbox;
    }

    public async Task<bool> StageSwapAsync(
        Domain.Entities.Trip trip,
        Vehicle oldVehicle,
        Vehicle newVehicle,
        IReadOnlyCollection<TripSeat> lockedSeats,
        IReadOnlyCollection<VehicleSwapBookingSeatImpact> bookingSeatImpacts,
        Guid actorUserId,
        string auditAction,
        string requestId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(oldVehicle);
        ArgumentNullException.ThrowIfNull(newVehicle);
        ArgumentNullException.ThrowIfNull(lockedSeats);
        ArgumentNullException.ThrowIfNull(bookingSeatImpacts);

        ValidateSwapIdentity(trip, oldVehicle, newVehicle, actorUserId);
        ValidateAuditAction(auditAction);
        var normalizedRequestId = NormalizeRequestId(requestId);
        if (oldVehicle.Id == newVehicle.Id)
        {
            return false;
        }

        var newLayout = ParseLayout(newVehicle);
        var seatsByNumber = BuildLockedSeatMap(trip.Id, lockedSeats);
        var orderedImpacts = ValidateAndOrderImpacts(bookingSeatImpacts, seatsByNumber, newLayout);

        await ReconcileAvailableSeatsAsync(trip.Id, lockedSeats, newLayout, cancellationToken);
        if (!trip.ChangeVehicle(newVehicle.Id))
        {
            return false;
        }

        var eventId = Guid.NewGuid();
        var integrationEvent = new TripVehicleSwappedIntegrationEvent(
            eventId,
            occurredAt,
            trip.Id,
            trip.OperatorId,
            oldVehicle.Id,
            newVehicle.Id,
            oldVehicle.LicensePlate,
            newVehicle.LicensePlate,
            trip.DepartureDateTime,
            trip.DriverUserId,
            trip.AssistantUserId,
            orderedImpacts);
        var payload = JsonSerializer.Serialize(integrationEvent, JsonOptions);

        await auditLogs.AddAsync(
            TripAuditLog.Create(
                Guid.NewGuid(),
                trip.Id,
                actorUserId,
                auditAction,
                JsonSerializer.Serialize(new
                {
                    changedFields = new[] { "vehicleId" },
                    before = new { vehicleId = oldVehicle.Id },
                    after = new { vehicleId = newVehicle.Id },
                    requestId = normalizedRequestId,
                }, JsonOptions),
                occurredAt),
            cancellationToken);
        await outbox.EnqueueAsync(TripVehicleSwappedIntegrationEvent.EventTypeValue, payload, cancellationToken);

        return true;
    }

    private async Task ReconcileAvailableSeatsAsync(
        Guid tripId,
        IReadOnlyCollection<TripSeat> lockedSeats,
        IReadOnlyDictionary<string, LayoutSeat> newLayout,
        CancellationToken cancellationToken)
    {
        var retainedNumbers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seat in lockedSeats.OrderBy(seat => seat.SeatNumber, StringComparer.Ordinal).ThenBy(seat => seat.Id))
        {
            if (seat.Status != TripSeatStatus.AVAILABLE)
            {
                retainedNumbers.Add(seat.SeatNumber);
                continue;
            }

            if (newLayout.TryGetValue(seat.SeatNumber, out var layoutSeat) && layoutSeat.IsPassenger)
            {
                seat.ReconfigureAvailable(layoutSeat.SeatType);
                retainedNumbers.Add(seat.SeatNumber);
            }
            else
            {
                tripSeats.Remove(seat);
            }
        }

        foreach (var layoutSeat in newLayout.Values
                     .Where(seat => seat.IsPassenger && !retainedNumbers.Contains(seat.SeatNumber))
                     .OrderBy(seat => seat.SeatNumber, StringComparer.Ordinal))
        {
            await tripSeats.AddAsync(
                TripSeat.Create(tripId, layoutSeat.SeatNumber, layoutSeat.SeatType),
                cancellationToken);
        }
    }

    private static IReadOnlyList<VehicleSwapBookingSeatImpact> ValidateAndOrderImpacts(
        IReadOnlyCollection<VehicleSwapBookingSeatImpact> impacts,
        IReadOnlyDictionary<string, TripSeat> seatsByNumber,
        IReadOnlyDictionary<string, LayoutSeat> newLayout)
    {
        var impactedSeatNumbers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var impact in impacts)
        {
            ArgumentNullException.ThrowIfNull(impact);
            if (!VehicleSwapBookingSeatImpact.IsApprovedReason(impact.Reason))
            {
                throw new ArgumentException("Seat impact reason is not approved.", nameof(impacts));
            }

            foreach (var seatNumber in impact.SeatNumbers)
            {
                if (!impactedSeatNumbers.Add(seatNumber))
                {
                    throw new ArgumentException("An impacted seat may be assigned to only one Booking impact.", nameof(impacts));
                }

                if (!seatsByNumber.TryGetValue(seatNumber, out var lockedSeat)
                    || lockedSeat.Status != TripSeatStatus.BOOKED)
                {
                    throw new ArgumentException("Booking impacts must address locked BOOKED seats.", nameof(impacts));
                }

                var expectedReason = ClassifyImpact(lockedSeat, newLayout);
                if (!string.Equals(expectedReason, impact.Reason, StringComparison.Ordinal))
                {
                    throw new ArgumentException("Booking impact reason does not match the locked seat and new layout.", nameof(impacts));
                }
            }
        }

        var missingBookedImpact = seatsByNumber.Values
            .Where(seat => seat.Status == TripSeatStatus.BOOKED && ClassifyImpact(seat, newLayout) is not null)
            .Select(seat => seat.SeatNumber)
            .FirstOrDefault(seatNumber => !impactedSeatNumbers.Contains(seatNumber));
        if (missingBookedImpact is not null)
        {
            throw new ArgumentException($"BOOKED seat {missingBookedImpact} is missing its Booking impact.", nameof(impacts));
        }

        return impacts
            .OrderBy(impact => impact.BookingId)
            .ThenBy(impact => impact.Reason, StringComparer.Ordinal)
            .ThenBy(impact => impact.SeatNumbers[0], StringComparer.Ordinal)
            .ToArray();
    }

    private static string? ClassifyImpact(
        TripSeat oldSeat,
        IReadOnlyDictionary<string, LayoutSeat> newLayout)
    {
        if (!newLayout.TryGetValue(oldSeat.SeatNumber, out var newSeat))
        {
            return VehicleSwapBookingSeatImpact.SeatRemoved;
        }

        if (!newSeat.IsPassenger)
        {
            return VehicleSwapBookingSeatImpact.SeatDisabled;
        }

        return PassengerRank(newSeat.SeatType) < PassengerRank(oldSeat.SeatType)
            ? VehicleSwapBookingSeatImpact.SeatTypeDowngraded
            : null;
    }

    private static int PassengerRank(TripSeatType seatType) => seatType switch
    {
        TripSeatType.STANDARD => 0,
        TripSeatType.SLEEPER_UPPER => 1,
        TripSeatType.SLEEPER_LOWER => 2,
        TripSeatType.VIP => 3,
        _ => throw new ArgumentException("DRIVER_AREA is not a passenger seat type.", nameof(seatType)),
    };

    private static IReadOnlyDictionary<string, LayoutSeat> ParseLayout(Vehicle vehicle)
    {
        SeatLayoutDto layout;
        try
        {
            layout = vehicle.SeatLayoutJson.Deserialize<SeatLayoutDto>(JsonOptions)
                ?? throw new ArgumentException("Vehicle seat layout cannot be parsed.", nameof(vehicle));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Vehicle seat layout cannot be parsed.", nameof(vehicle), exception);
        }

        var result = new Dictionary<string, LayoutSeat>(StringComparer.Ordinal);
        foreach (var seat in layout.Seats)
        {
            var seatNumber = NormalizeSeatNumber(seat.SeatNumber);
            if (!Enum.TryParse<TripSeatType>(seat.Type, ignoreCase: true, out var seatType)
                || !Enum.IsDefined(seatType))
            {
                throw new ArgumentException($"Unknown seat type '{seat.Type}'.", nameof(vehicle));
            }

            if (!result.TryAdd(
                    seatNumber,
                    new LayoutSeat(seatNumber, seatType, !seat.Disabled && seatType != TripSeatType.DRIVER_AREA)))
            {
                throw new ArgumentException($"Duplicate seat number '{seatNumber}' in vehicle layout.", nameof(vehicle));
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, TripSeat> BuildLockedSeatMap(
        Guid tripId,
        IReadOnlyCollection<TripSeat> lockedSeats)
    {
        var result = new Dictionary<string, TripSeat>(StringComparer.Ordinal);
        foreach (var seat in lockedSeats)
        {
            if (seat.TripId != tripId)
            {
                throw new ArgumentException("Locked seat belongs to another Trip.", nameof(lockedSeats));
            }

            if (!result.TryAdd(seat.SeatNumber, seat))
            {
                throw new ArgumentException("Locked seats contain a duplicate normalized seat number.", nameof(lockedSeats));
            }
        }

        return result;
    }

    private static void ValidateSwapIdentity(
        Domain.Entities.Trip trip,
        Vehicle oldVehicle,
        Vehicle newVehicle,
        Guid actorUserId)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("Actor user id cannot be empty.", nameof(actorUserId));
        }

        if (trip.VehicleId != oldVehicle.Id)
        {
            throw new ArgumentException("Old vehicle does not match the locked Trip.", nameof(oldVehicle));
        }

        if (trip.OperatorId != oldVehicle.OperatorId || trip.OperatorId != newVehicle.OperatorId)
        {
            throw new ArgumentException("Both vehicles must belong to the Trip operator.");
        }
    }

    private static void ValidateAuditAction(string auditAction)
    {
        if (auditAction is not TripAuditAction.TripVehicleSwapped
            and not TripAuditAction.DriverScheduleCascadeApplied)
        {
            throw new ArgumentException("Audit action is not approved for a vehicle swap.", nameof(auditAction));
        }
    }

    private static string NormalizeRequestId(string requestId)
    {
        var normalized = requestId?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Request id cannot be blank.", nameof(requestId));
        }

        return normalized;
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

    private sealed record LayoutSeat(string SeatNumber, TripSeatType SeatType, bool IsPassenger);
}
