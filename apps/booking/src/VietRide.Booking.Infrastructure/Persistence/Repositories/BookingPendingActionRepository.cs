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
