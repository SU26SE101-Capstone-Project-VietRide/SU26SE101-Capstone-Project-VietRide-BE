using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Entities;

namespace VietRide.Booking.IntegrationTests.Jobs;

public sealed class CoordinatedPendingActionRepository(
    IBookingPendingActionRepository inner,
    TaskCompletionSource actionLockRequested,
    TaskCompletionSource releaseActionLock) : IBookingPendingActionRepository
{
    public Task<BookingPendingAction?> GetByIdAsync(Guid id, CancellationToken ct)
        => inner.GetByIdAsync(id, ct);

    public Task<BookingPendingAction> AddAsync(BookingPendingAction entity, CancellationToken ct)
        => inner.AddAsync(entity, ct);

    public void Update(BookingPendingAction entity) => inner.Update(entity);

    public void Remove(BookingPendingAction entity) => inner.Remove(entity);

    public IQueryable<BookingPendingAction> Query() => inner.Query();

    public IQueryable<BookingPendingAction> QueryNoTracking() => inner.QueryNoTracking();

    public async Task<BookingPendingAction?> GetByIdForUpdateAsync(
        Guid actionId,
        CancellationToken ct = default)
        => await inner.GetByIdForUpdateAsync(actionId, ct);

    public async Task<BookingPendingAction?> GetByIdForUpdateSkipLockedAsync(
        Guid actionId,
        CancellationToken ct = default)
    {
        actionLockRequested.TrySetResult();
        await releaseActionLock.Task.WaitAsync(ct);
        return await inner.GetByIdForUpdateSkipLockedAsync(actionId, ct);
    }

    public Task<IReadOnlyList<BookingPendingAction>> GetActiveByTripForUpdateAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken ct = default)
        => inner.GetActiveByTripForUpdateAsync(tripId, operatorId, ct);

    public Task<BookingPendingAction?> GetActiveByBookingIdAsync(
        Guid bookingId,
        CancellationToken ct = default)
        => inner.GetActiveByBookingIdAsync(bookingId, ct);

    public Task<BookingPendingAction?> GetActiveByBookingIdForUpdateAsync(
        Guid bookingId,
        CancellationToken ct = default)
        => inner.GetActiveByBookingIdForUpdateAsync(bookingId, ct);

    public Task<IReadOnlyList<BookingPendingAction>> GetByBookingAndSourceEventAsync(
        Guid bookingId,
        Guid sourceEventId,
        CancellationToken ct = default)
        => inner.GetByBookingAndSourceEventAsync(bookingId, sourceEventId, ct);

    public Task<IReadOnlyList<BookingPendingAction>> GetExpiredStopDisabledCandidatesAsync(
        DateTimeOffset now,
        CancellationToken ct = default)
        => inner.GetExpiredStopDisabledCandidatesAsync(now, ct);
}
