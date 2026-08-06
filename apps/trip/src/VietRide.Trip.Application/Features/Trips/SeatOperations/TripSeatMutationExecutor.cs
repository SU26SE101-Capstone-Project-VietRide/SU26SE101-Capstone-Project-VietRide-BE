using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Trips.GetTripSeatMap;
using VietRide.Trip.Domain.Constants;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Trips.SeatOperations;

public sealed class TripSeatMutationExecutor
{
    private readonly ITripRepository trips;
    private readonly ITripSeatRepository seats;
    private readonly ITripAuditLogRepository auditLogs;
    private readonly IUnitOfWork unitOfWork;
    private readonly IClock clock;
    private readonly MediatR.ISender sender;

    public TripSeatMutationExecutor(
        ITripRepository trips,
        ITripSeatRepository seats,
        ITripAuditLogRepository auditLogs,
        IUnitOfWork unitOfWork,
        IClock clock,
        MediatR.ISender sender)
    {
        this.trips = trips;
        this.seats = seats;
        this.auditLogs = auditLogs;
        this.unitOfWork = unitOfWork;
        this.clock = clock;
        this.sender = sender;
    }

    public Task<TripSeatMapDto> DisableAsync(
        DisableTripSeatCommand request,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            request.TripId,
            request.OperatorId,
            request.ActorUserId,
            request.SeatNumber,
            TripAuditAction.TripSeatDisabled,
            request.RequestId,
            request.Reason,
            (seat, reason) => seat.Disable(reason ?? string.Empty),
            cancellationToken);

    public Task<TripSeatMapDto> EnableAsync(
        EnableTripSeatCommand request,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            request.TripId,
            request.OperatorId,
            request.ActorUserId,
            request.SeatNumber,
            TripAuditAction.TripSeatEnabled,
            request.RequestId,
            null,
            (seat, _) => seat.Enable(),
            cancellationToken);

    private async Task<TripSeatMapDto> ExecuteAsync(
        Guid tripId,
        Guid operatorId,
        Guid actorUserId,
        string seatNumber,
        string auditAction,
        string requestId,
        string? reason,
        Action<TripSeat, string?> transition,
        CancellationToken cancellationToken)
    {
        var trip = await trips.QueryNoTracking()
            .FirstOrDefaultAsync(item => item.Id == tripId && item.OperatorId == operatorId, cancellationToken);
        if (trip is null)
        {
            throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
        }

        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var seat = await seats.AcquireForUpdateAsync(tripId, seatNumber.Trim(), cancellationToken);
            if (seat is null)
            {
                throw new CodedNotFoundException("TRIP_SEAT_NOT_FOUND", "Trip seat was not found.");
            }

            var beforeStatus = seat.Status;
            EnsureTransitionAllowed(seat, auditAction);
            try
            {
                transition(seat, reason);
            }
            catch (ArgumentException exception)
            {
                throw new CodedValidationException(
                    "VALIDATION_ERROR",
                    exception.Message,
                    [new ValidationError("reason", exception.Message)]);
            }

            await auditLogs.AddAsync(
                TripAuditLog.Create(
                    Guid.NewGuid(),
                    tripId,
                    actorUserId,
                    auditAction,
                    JsonSerializer.Serialize(new
                    {
                        seatNumber = seat.SeatNumber,
                        beforeStatus = beforeStatus.ToString(),
                        afterStatus = seat.Status.ToString(),
                        reason,
                        requestId,
                    }),
                    clock.UtcNow),
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return await sender.Send(new GetTripSeatMapQuery(tripId), cancellationToken);
        }, cancellationToken);
    }

    private static void EnsureTransitionAllowed(TripSeat seat, string auditAction)
    {
        if (seat.Status is TripSeatStatus.HELD or TripSeatStatus.BOOKED)
        {
            throw new CodedConflictException("TRIP_SEAT_IN_USE", "The trip seat is already held or booked.");
        }

        var expectedStatus = auditAction == TripAuditAction.TripSeatDisabled
            ? TripSeatStatus.AVAILABLE
            : TripSeatStatus.UNAVAILABLE;
        if (seat.Status != expectedStatus)
        {
            throw new CodedConflictException(
                "TRIP_SEAT_STATE_CONFLICT",
                $"The trip seat must be {expectedStatus} for this operation.");
        }
    }
}
