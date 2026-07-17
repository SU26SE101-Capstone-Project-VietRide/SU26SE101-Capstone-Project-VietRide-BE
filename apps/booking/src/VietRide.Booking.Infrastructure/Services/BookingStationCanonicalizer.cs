using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Services;

namespace VietRide.Booking.Infrastructure.Services;

internal sealed class BookingStationCanonicalizer : IBookingStationCanonicalizer
{
    private readonly BookingDbContext _db;

    public BookingStationCanonicalizer(BookingDbContext db) => _db = db;

    public async Task<StationCanonicalizationResult> LockAndResolveAsync(
        IReadOnlyCollection<Guid> stationIds,
        CancellationToken cancellationToken = default)
    {
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Booking Station canonicalization requires an ambient transaction.");

        var orderedIds = stationIds
            .Where(stationId => stationId != Guid.Empty)
            .Distinct()
            .OrderBy(stationId => stationId.ToString("D"), StringComparer.Ordinal)
            .ToArray();
        foreach (var stationId in orderedIds)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended('booking-station:' || {stationId}::text, 0))",
                cancellationToken);
        }

        var redirectRows = await _db.BookingStationRedirects
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var graph = BookingStationRedirectGraph.ToDictionary(redirectRows);
        var canonicalByStationId = orderedIds.ToDictionary(
            stationId => stationId,
            stationId => BookingStationRedirectGraph.ResolvePath(stationId, graph).TerminalStationId);
        return new StationCanonicalizationResult(
            canonicalByStationId,
            orderedIds.ToHashSet());
    }
}
