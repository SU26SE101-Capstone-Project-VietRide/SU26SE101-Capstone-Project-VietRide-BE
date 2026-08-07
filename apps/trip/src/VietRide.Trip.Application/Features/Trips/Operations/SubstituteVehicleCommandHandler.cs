using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Events;
using VietRide.Trip.Application.Features.Vehicles;
using VietRide.Trip.Domain.Constants;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed class SubstituteVehicleCommandHandler
    : IRequestHandler<SubstituteVehicleCommand, SubstituteVehicleResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITripRepository trips;
    private readonly IVehicleRepository vehicles;
    private readonly ITripSeatRepository tripSeats;
    private readonly ITripStopRepository tripStops;
    private readonly ITripStopFareRepository tripStopFares;
    private readonly ITripAuditLogRepository auditLogs;
    private readonly IBookingImpactClient bookingImpact;
    private readonly IIdentityInternalClient identity;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IUnitOfWork unitOfWork;
    private readonly IClock clock;
    private readonly IRouteChangeProposalLifecycleService? routeChangeProposals;

    public SubstituteVehicleCommandHandler(
        ITripRepository trips,
        IVehicleRepository vehicles,
        ITripSeatRepository tripSeats,
        ITripStopRepository tripStops,
        ITripStopFareRepository tripStopFares,
        ITripAuditLogRepository auditLogs,
        IBookingImpactClient bookingImpact,
        IIdentityInternalClient identity,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IClock clock,
        IRouteChangeProposalLifecycleService? routeChangeProposals = null)
    {
        this.trips = trips;
        this.vehicles = vehicles;
        this.tripSeats = tripSeats;
        this.tripStops = tripStops;
        this.tripStopFares = tripStopFares;
        this.auditLogs = auditLogs;
        this.bookingImpact = bookingImpact;
        this.identity = identity;
        this.outbox = outbox;
        this.unitOfWork = unitOfWork;
        this.clock = clock;
        this.routeChangeProposals = routeChangeProposals;
    }

    public async Task<SubstituteVehicleResponse> Handle(
        SubstituteVehicleCommand request,
        CancellationToken cancellationToken)
    {
        var preflightTrip = await trips.QueryNoTracking()
            .SingleOrDefaultAsync(
                trip => trip.Id == request.TripId && trip.OperatorId == request.OperatorId,
                cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
        var replacementVehicle = await vehicles.GetOwnedByIdAsync(
            request.OperatorId,
            request.ReplacementVehicleId,
            cancellationToken)
            ?? throw new CodedNotFoundException("VEHICLE_NOT_FOUND", "Replacement vehicle was not found.");
        EnsureVehicleActive(replacementVehicle);

        var driverId = request.ReplacementCrewSpecified
            ? request.ReplacementDriverId!.Value
            : preflightTrip.DriverUserId;
        var assistantId = request.ReplacementCrewSpecified
            ? request.ReplacementAssistantId
            : preflightTrip.AssistantUserId;
        await ValidateCrewAsync(request.OperatorId, driverId, assistantId, cancellationToken);

        // Booking owns eligibility. This call intentionally completes before the Trip transaction starts.
        var impact = await bookingImpact.GetVehicleSubstitutionImpactAsync(
            request.TripId,
            request.OperatorId,
            cancellationToken);

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var oldTrip = await trips.GetForUpdateAsync(request.TripId, cancellationToken);
            if (oldTrip is null || oldTrip.OperatorId != request.OperatorId)
            {
                throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
            }

            if (!TripVehicleSubstitutionPolicy.CanSubstitute(oldTrip.Status))
            {
                throw new CodedConflictException(
                    "TRIP_NOT_SUBSTITUTABLE",
                    "Vehicle substitution requires an in-progress Trip.");
            }

            var disruptedAt = clock.UtcNow;
            if (request.EstimatedRecoveryDepartureAt <= disruptedAt)
            {
                throw new CodedValidationException(
                    "VALIDATION_ERROR",
                    "Estimated recovery departure must be later than disruptedAt.",
                    [new ValidationError(
                        "estimatedRecoveryDepartureAt",
                        "must be later than disruptedAt")]);
            }

            var lockedVehicles = await vehicles.AcquireForVehicleSwapAsync(
                request.OperatorId,
                [request.ReplacementVehicleId],
                cancellationToken);
            replacementVehicle = lockedVehicles.SingleOrDefault()
                ?? throw new CodedNotFoundException(
                    "VEHICLE_NOT_FOUND",
                    "Replacement vehicle was not found.");
            EnsureVehicleActive(replacementVehicle);

            await EnsureNoConflictsAsync(
                request,
                driverId,
                assistantId,
                cancellationToken);

            var oldSeats = await tripSeats.QueryNoTracking()
                .Where(seat => seat.TripId == oldTrip.Id)
                .OrderBy(seat => seat.SeatNumber)
                .ToArrayAsync(cancellationToken);
            var oldStops = await tripStops.QueryNoTracking()
                .Where(stop => stop.TripId == oldTrip.Id)
                .OrderBy(stop => stop.OrderIndex)
                .ToArrayAsync(cancellationToken);
            var oldFares = await tripStopFares.QueryNoTracking()
                .Where(fare => fare.TripId == oldTrip.Id)
                .ToArrayAsync(cancellationToken);

            var recoveryDelay = request.EstimatedRecoveryDepartureAt - disruptedAt;
            var newTrip = Domain.Entities.Trip.Create(
                oldTrip.OperatorId,
                oldTrip.RouteId,
                replacementVehicle.Id,
                driverId,
                assistantId,
                driverScheduleId: null,
                request.EstimatedRecoveryDepartureAt,
                oldTrip.EstimatedArrivalTime + recoveryDelay,
                TripSource.VEHICLE_SUBSTITUTION,
                oldTrip.BaseFare,
                replacementVehicle.MaxCargoWeightKg,
                replacementVehicle.MaxCargoVolumeM3,
                oldTrip.EstimatedPassengerLuggageKg,
                notes: oldTrip.Notes,
                seatLayoutSnapshotJson: replacementVehicle.SeatLayoutJson);
            newTrip.MarkBoarding(disruptedAt);
            await trips.AddAsync(newTrip, cancellationToken);

            var mappings = await CreateSeatsAndMappingsAsync(
                newTrip.Id,
                replacementVehicle,
                oldSeats,
                impact,
                cancellationToken);
            await CopyPendingStopsAndFaresAsync(
                newTrip.Id,
                oldStops,
                oldFares,
                recoveryDelay,
                cancellationToken);

            oldTrip.SubstituteVehicle(disruptedAt, request.Reason);
            if (routeChangeProposals is not null)
                await routeChangeProposals.ExpirePendingForTripAsync(oldTrip.Id, disruptedAt, cancellationToken);
            await auditLogs.AddAsync(
                TripAuditLog.Create(
                    Guid.NewGuid(),
                    oldTrip.Id,
                    request.ActorUserId,
                    TripAuditAction.VehicleSubstitutionTriggered,
                    JsonSerializer.Serialize(new
                    {
                        replacementTripId = newTrip.Id,
                        replacementVehicleId = replacementVehicle.Id,
                        reason = request.Reason.Trim(),
                    }, JsonOptions),
                    disruptedAt),
                cancellationToken);

            var substitutionId = Guid.NewGuid();
            var substituted = new TripVehicleSubstitutedIntegrationEvent(
                substitutionId,
                disruptedAt,
                substitutionId,
                disruptedAt,
                oldTrip.OperatorId,
                oldTrip.Id,
                TripStatus.DISRUPTED.ToString(),
                oldTrip.VehicleId,
                newTrip.Id,
                TripStatus.BOARDING.ToString(),
                replacementVehicle.Id,
                replacementVehicle.LicensePlate,
                newTrip.DepartureDateTime,
                request.ActorUserId,
                request.Reason.Trim(),
                request.NotifyPassengers,
                mappings);
            await outbox.EnqueueAsync(
                substitutionId,
                TripVehicleSubstitutedIntegrationEvent.EventType,
                JsonSerializer.Serialize(substituted, JsonOptions),
                cancellationToken);

            var disruptedEventId = Guid.NewGuid();
            var disrupted = new TripDisruptedIntegrationEvent(
                disruptedEventId,
                oldTrip.Id,
                oldTrip.OperatorId,
                disruptedAt,
                hasSubstitution: true,
                request.Reason.Trim());
            await outbox.EnqueueAsync(
                disruptedEventId,
                disrupted.EventType,
                JsonSerializer.Serialize(disrupted, JsonOptions),
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            return new SubstituteVehicleResponse(
                substitutionId,
                oldTrip.Id,
                TripStatus.DISRUPTED.ToString(),
                newTrip.Id,
                TripStatus.BOARDING.ToString(),
                newTrip.DepartureDateTime,
                "QUEUED",
                impact.Bookings.Count,
                mappings.Count,
                mappings.Count(mapping => mapping.NewSeatNumber is null));
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<IReadOnlyList<TripVehicleSubstitutedIntegrationEvent.Mapping>> CreateSeatsAndMappingsAsync(
        Guid newTripId,
        Vehicle replacementVehicle,
        IReadOnlyCollection<TripSeat> oldSeats,
        VehicleSubstitutionImpactProjection impact,
        CancellationToken cancellationToken)
    {
        var layout = ParsePassengerLayout(replacementVehicle);
        var newSeats = layout
            .Select(seat => TripSeat.Create(newTripId, seat.SeatNumber, seat.SeatType))
            .ToDictionary(seat => seat.SeatNumber, StringComparer.Ordinal);
        foreach (var seat in newSeats.Values)
        {
            await tripSeats.AddAsync(seat, cancellationToken);
        }

        var oldSeatsByNumber = oldSeats.ToDictionary(seat => seat.SeatNumber, StringComparer.Ordinal);
        var available = layout.ToList();
        var mappings = new List<TripVehicleSubstitutedIntegrationEvent.Mapping>();
        foreach (var booking in impact.Bookings.OrderBy(item => item.BookingId))
        {
            foreach (var passenger in booking.Passengers.OrderBy(item => item.PassengerId))
            {
                var originalSeatNumber = NormalizeNullableSeat(passenger.OriginalSeatNumber);
                var preferredType = originalSeatNumber is not null
                    && oldSeatsByNumber.TryGetValue(originalSeatNumber, out var oldSeat)
                        ? oldSeat.SeatType
                        : (TripSeatType?)null;
                var selected = preferredType.HasValue
                    ? available.FirstOrDefault(seat => seat.SeatType == preferredType.Value)
                    : null;
                selected ??= available.FirstOrDefault();
                if (selected is not null)
                {
                    available.Remove(selected);
                    newSeats[selected.SeatNumber].MarkHeld();
                    newSeats[selected.SeatNumber].MarkBooked();
                }

                mappings.Add(new TripVehicleSubstitutedIntegrationEvent.Mapping(
                    booking.BookingId,
                    passenger.PassengerId,
                    originalSeatNumber,
                    selected?.SeatNumber,
                    passenger.BoardingStatus));
            }
        }

        return mappings;
    }

    private async Task CopyPendingStopsAndFaresAsync(
        Guid newTripId,
        IReadOnlyCollection<TripStop> oldStops,
        IReadOnlyCollection<TripStopFare> oldFares,
        TimeSpan recoveryDelay,
        CancellationToken cancellationToken)
    {
        var copiedStopIds = new HashSet<Guid>();
        foreach (var stop in oldStops.Where(stop => stop.Status == TripStopStatus.PENDING))
        {
            copiedStopIds.Add(stop.StopId);
            await tripStops.AddAsync(
                TripStop.Create(
                    newTripId,
                    stop.StopId,
                    stop.OrderIndex,
                    stop.EstimatedArrivalTime + recoveryDelay,
                    stop.AllowPickup,
                    stop.AllowDropoff,
                    stop.DistanceFromOriginKm),
                cancellationToken);
        }

        foreach (var fare in oldFares.Where(fare => copiedStopIds.Contains(fare.StopId)))
        {
            await tripStopFares.AddAsync(
                TripStopFare.Create(
                    newTripId,
                    fare.StopId,
                    fare.FareFromThisStop,
                    fare.Source),
                cancellationToken);
        }
    }

    private async Task ValidateCrewAsync(
        Guid operatorId,
        Guid driverId,
        Guid? assistantId,
        CancellationToken cancellationToken)
    {
        await ValidateCrewMemberAsync(driverId, "DRIVER", operatorId, "replacementCrew.driverId", cancellationToken);
        if (assistantId.HasValue)
        {
            await ValidateCrewMemberAsync(
                assistantId.Value,
                "ASSISTANT",
                operatorId,
                "replacementCrew.assistantId",
                cancellationToken);
        }
    }

    private async Task ValidateCrewMemberAsync(
        Guid userId,
        string role,
        Guid operatorId,
        string field,
        CancellationToken cancellationToken)
    {
        var user = await identity.GetUserAsync(userId, cancellationToken);
        if (!user.Found
            || user.Id != userId
            || user.Role != role
            || user.OperatorId != operatorId
            || user.Status != "ACTIVE")
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Replacement crew member is not active in the required operator role.",
                [new ValidationError(field, "must be an active operator-owned " + role)]);
        }
    }

    private async Task EnsureNoConflictsAsync(
        SubstituteVehicleCommand request,
        Guid driverId,
        Guid? assistantId,
        CancellationToken cancellationToken)
    {
        if (await trips.HasVehicleConflictAsync(
                request.ReplacementVehicleId,
                request.EstimatedRecoveryDepartureAt,
                request.TripId,
                cancellationToken))
        {
            throw new CodedConflictException(
                "TRIP_VEHICLE_CONFLICT",
                "Replacement vehicle has a conflicting Trip.");
        }

        var crewConflict = await trips.QueryNoTracking().AnyAsync(
            trip => trip.Id != request.TripId
                && trip.DepartureDateTime == request.EstimatedRecoveryDepartureAt
                && trip.Status != TripStatus.CANCELLED
                && trip.Status != TripStatus.COMPLETED
                && (trip.DriverUserId == driverId
                    || (assistantId.HasValue && trip.AssistantUserId == assistantId.Value)),
            cancellationToken);
        if (crewConflict)
        {
            throw new CodedConflictException(
                "TRIP_CREW_CONFLICT",
                "Replacement crew has a conflicting Trip.");
        }
    }

    private static IReadOnlyList<LayoutSeat> ParsePassengerLayout(Vehicle vehicle)
    {
        var layout = vehicle.SeatLayoutJson.Deserialize<SeatLayoutDto>(JsonOptions)
            ?? throw new CodedValidationException("VALIDATION_ERROR", "Vehicle seat layout is invalid.");
        var seats = new List<LayoutSeat>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in layout.Seats.OrderBy(item => item.SeatNumber, StringComparer.Ordinal))
        {
            var seatNumber = item.SeatNumber.Trim().ToUpperInvariant();
            if (!seen.Add(seatNumber))
            {
                throw new CodedValidationException("VALIDATION_ERROR", "Vehicle seat layout contains duplicate seats.");
            }

            if (!Enum.TryParse<TripSeatType>(item.Type, true, out var seatType)
                || !Enum.IsDefined(seatType))
            {
                throw new CodedValidationException("VALIDATION_ERROR", "Vehicle seat layout contains an unknown seat type.");
            }

            if (!item.Disabled && seatType != TripSeatType.DRIVER_AREA)
            {
                seats.Add(new LayoutSeat(seatNumber, seatType));
            }
        }

        return seats;
    }

    private static void EnsureVehicleActive(Vehicle vehicle)
    {
        if (!vehicle.IsActive || vehicle.Status != VehicleStatus.ACTIVE || vehicle.DeletedAt.HasValue)
        {
            throw new CodedValidationException(
                "VEHICLE_NOT_ACTIVE",
                "Replacement vehicle must be active.");
        }
    }

    private static string? NormalizeNullableSeat(string? seatNumber)
        => string.IsNullOrWhiteSpace(seatNumber)
            ? null
            : seatNumber.Trim().ToUpperInvariant();

    private sealed record LayoutSeat(string SeatNumber, TripSeatType SeatType);
}
