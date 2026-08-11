namespace VietRide.Trip.Application.Abstractions.Services;

public interface ITripAssignmentAlertStore
{
    Task<bool> TryAddStartBlockedAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken cancellationToken = default);
}
