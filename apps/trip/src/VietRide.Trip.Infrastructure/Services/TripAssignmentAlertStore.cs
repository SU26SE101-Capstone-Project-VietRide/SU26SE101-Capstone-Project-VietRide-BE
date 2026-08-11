using Microsoft.EntityFrameworkCore;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Services;

internal sealed class TripAssignmentAlertStore : ITripAssignmentAlertStore
{
    private readonly TripDbContext db;

    public TripAssignmentAlertStore(TripDbContext db)
    {
        this.db = db;
    }

    public async Task<bool> TryAddStartBlockedAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        var alertType = ShuttleDispatchAlertType.ASSIGNMENT_START_BLOCKED;
        if (await db.ShuttleDispatchAlerts.AnyAsync(
                alert => alert.MainTripId == tripId && alert.AlertType == alertType.ToString(),
                cancellationToken))
        {
            return false;
        }

        db.ShuttleDispatchAlerts.Add(ShuttleDispatchAlert.Create(tripId, operatorId, alertType));
        return true;
    }
}
