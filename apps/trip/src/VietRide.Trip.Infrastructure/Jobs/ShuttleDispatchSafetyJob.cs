using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Jobs;

public sealed class ShuttleDispatchSafetyJob
{
    private const string AutoCutoffReason = "AUTO_UNFULFILLED_CUTOFF";
    private readonly TripDbContext _db;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public ShuttleDispatchSafetyJob(
        TripDbContext db,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _db = db;
        _outbox = outbox;
        _clock = clock;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ScanAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var tripIds = await _db.ShuttlePassengers.AsNoTracking()
            .Where(passenger => passenger.Status == ShuttlePassenger.PendingAssignmentStatus)
            .Join(_db.Trips.AsNoTracking().Where(trip => trip.Status == TripStatus.SCHEDULED
                    && trip.DepartureDateTime <= now.AddMinutes(120)),
                passenger => passenger.MainTripId,
                trip => trip.Id,
                (passenger, trip) => trip.Id)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        foreach (var tripId in tripIds)
        {
            await ProcessTripAsync(tripId, now, cancellationToken);
        }
    }

    private async Task ProcessTripAsync(Guid tripId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var trip = await _db.Trips.SingleOrDefaultAsync(x => x.Id == tripId, cancellationToken);
        if (trip is null || trip.Status != TripStatus.SCHEDULED)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        var pending = await _db.ShuttlePassengers
            .FromSqlInterpolated($"SELECT * FROM vietride_trip.shuttle_passengers WHERE main_trip_id = {tripId} AND status = 'PENDING_ASSIGNMENT' FOR UPDATE")
            .ToArrayAsync(cancellationToken);
        if (pending.Length == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        var minutes = (trip.DepartureDateTime - now).TotalMinutes;
        if (minutes <= 30)
        {
            if (!await TryAddAlertAsync(trip, ShuttleDispatchAlertType.AUTO_CUTOFF, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return;
            }

            foreach (var manifest in pending)
            {
                manifest.Cancel(AutoCutoffReason);
            }

            var stationId = await _db.Routes.AsNoTracking()
                .Where(route => route.Id == trip.RouteId)
                .Select(route => route.OriginStationId)
                .SingleAsync(cancellationToken);
            foreach (var group in pending.Where(x => x.BookingId.HasValue).GroupBy(x => x.BookingId!.Value))
            {
                var passengerUserId = group.Select(x => x.PassengerUserId).FirstOrDefault(x => x.HasValue);
                await _outbox.EnqueueAsync("trip.shuttle.unfulfilled", JsonSerializer.Serialize(new
                {
                    mainTripId = trip.Id,
                    bookingId = group.Key,
                    passengerUserId,
                    stationId,
                    reason = AutoCutoffReason,
                }), cancellationToken);
            }
        }
        else
        {
            var alertType = minutes <= 60
                ? ShuttleDispatchAlertType.WARNING_60
                : ShuttleDispatchAlertType.WARNING_120;
            if (!await TryAddAlertAsync(trip, alertType, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return;
            }

            await _outbox.EnqueueAsync("trip.shuttle.warning_issued", JsonSerializer.Serialize(new
            {
                mainTripId = trip.Id,
                operatorId = trip.OperatorId,
                alertType = alertType.ToString(),
                pendingBookingCount = pending.Where(x => x.BookingId.HasValue).Select(x => x.BookingId).Distinct().Count(),
                pendingPassengerCount = pending.Length,
                hardCutoffAt = trip.DepartureDateTime.AddMinutes(-30),
            }), cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<bool> TryAddAlertAsync(
        Domain.Entities.Trip trip,
        ShuttleDispatchAlertType alertType,
        CancellationToken cancellationToken)
    {
        var exists = await _db.ShuttleDispatchAlerts.AnyAsync(
            alert => alert.MainTripId == trip.Id && alert.AlertType == alertType.ToString(),
            cancellationToken);
        if (exists)
        {
            return false;
        }

        _db.ShuttleDispatchAlerts.Add(ShuttleDispatchAlert.Create(trip.Id, trip.OperatorId, alertType));
        return true;
    }
}
