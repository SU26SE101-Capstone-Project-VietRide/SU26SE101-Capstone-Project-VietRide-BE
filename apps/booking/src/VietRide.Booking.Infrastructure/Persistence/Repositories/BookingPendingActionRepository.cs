using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Entities;

namespace VietRide.Booking.Infrastructure.Persistence.Repositories;

internal sealed class BookingPendingActionRepository(BookingDbContext db) : IBookingPendingActionRepository
{
    public Task<BookingPendingAction?> GetByIdAsync(Guid id, CancellationToken ct)
        => db.BookingPendingActions.FirstOrDefaultAsync(x => x.Id == id, ct);
    public async Task<BookingPendingAction> AddAsync(BookingPendingAction entity, CancellationToken ct)
    {
        await db.BookingPendingActions.AddAsync(entity, ct);
        return entity;
    }
    public void Update(BookingPendingAction entity) => db.BookingPendingActions.Update(entity);
    public void Remove(BookingPendingAction entity) => db.BookingPendingActions.Remove(entity);
    public IQueryable<BookingPendingAction> Query() => db.BookingPendingActions;
    public IQueryable<BookingPendingAction> QueryNoTracking() => db.BookingPendingActions.AsNoTracking();

    public Task<BookingPendingAction?> GetByIdForUpdateAsync(
        Guid actionId,
        CancellationToken ct = default)
        => db.BookingPendingActions
            .FromSqlInterpolated($"""
                SELECT *
                FROM vietride_booking.booking_pending_actions
                WHERE id = {actionId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<BookingPendingAction>> GetActiveByTripForUpdateAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken ct = default)
        => await db.BookingPendingActions
            .FromSqlInterpolated($"""
                SELECT action.*
                FROM vietride_booking.booking_pending_actions AS action
                INNER JOIN vietride_booking.bookings AS booking ON booking.id = action.booking_id
                WHERE booking.trip_id = {tripId}
                  AND booking.operator_id = {operatorId}
                  AND action.resolved_at IS NULL
                ORDER BY action.id
                FOR UPDATE OF action
                """)
            .ToListAsync(ct);

    public Task<BookingPendingAction?> GetActiveByBookingIdAsync(Guid bookingId, CancellationToken ct = default)
        => db.BookingPendingActions
            .FirstOrDefaultAsync(action => action.BookingId == bookingId && action.ResolvedAt == null, ct);

    public async Task<IReadOnlyList<BookingPendingAction>> GetByBookingAndSourceEventAsync(
        Guid bookingId,
        Guid sourceEventId,
        CancellationToken ct = default)
    {
        var candidates = await db.BookingPendingActions
            .Where(action => action.BookingId == bookingId && action.Metadata != null)
            .OrderBy(action => action.CreatedAt)
            .ThenBy(action => action.Id)
            .ToListAsync(ct);

        return candidates.Where(action => HasSourceEventId(action.Metadata, sourceEventId)).ToArray();
    }

    private static bool HasSourceEventId(string? metadata, Guid sourceEventId)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(metadata);
            return document.RootElement.TryGetProperty("sourceEventId", out var value)
                && value.ValueKind == JsonValueKind.String
                && value.TryGetGuid(out var storedId)
                && storedId == sourceEventId;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
