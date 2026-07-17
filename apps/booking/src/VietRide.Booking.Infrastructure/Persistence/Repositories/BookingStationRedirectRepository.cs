using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Infrastructure.Services;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Infrastructure.Persistence.Repositories;

internal sealed class BookingStationRedirectRepository : IBookingStationRedirectRepository
{
    private const int MaximumLockSetAttempts = 3;
    private readonly BookingDbContext _db;
    private readonly IBookingRepository _bookings;
    private readonly IClock _clock;

    public BookingStationRedirectRepository(
        BookingDbContext db,
        IBookingRepository bookings,
        IClock clock)
    {
        _db = db;
        _bookings = bookings;
        _clock = clock;
    }

    public async Task<BookingStationMergeApplicationResult> ApplyMergeAsync(
        Guid sourceEventId,
        DateTimeOffset occurredAt,
        Guid primaryStationId,
        Guid duplicateStationId,
        CancellationToken cancellationToken = default)
    {
        ValidateInput(sourceEventId, primaryStationId, duplicateStationId);
        if (_db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Station merge consumer cannot run inside an ambient transaction.");

        var graphRows = await LoadGraphAsync(cancellationToken);
        for (var attempt = 1; attempt <= MaximumLockSetAttempts; attempt++)
        {
            var preLockPlan = BuildLockPlan(graphRows, primaryStationId, duplicateStationId);
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                foreach (var stationId in preLockPlan.RequiredLockIds)
                    await AcquireStationLockAsync(stationId, cancellationToken);

                var lockedGraphRows = await LoadGraphAsync(cancellationToken);
                var lockedPlan = BuildLockPlan(lockedGraphRows, primaryStationId, duplicateStationId);
                if (lockedPlan.RequiredLockIds.Any(stationId => !preLockPlan.RequiredLockIds.Contains(stationId)))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _db.ChangeTracker.Clear();
                    graphRows = lockedGraphRows;
                    continue;
                }

                var replay = lockedGraphRows.SingleOrDefault(redirect => redirect.SourceEventId == sourceEventId);
                if (replay is not null)
                {
                    if (replay.DuplicateStationId != duplicateStationId
                        || replay.CanonicalStationId != lockedPlan.CanonicalPrimaryStationId)
                    {
                        throw new InvalidOperationException(
                            "Station merge event id was replayed with a different payload.");
                    }

                    await transaction.CommitAsync(cancellationToken);
                    return BookingStationMergeApplicationResult.Replay(replay.CanonicalStationId);
                }

                if (lockedGraphRows.Any(redirect => redirect.DuplicateStationId == duplicateStationId))
                {
                    throw new InvalidOperationException(
                        "A conflicting Station merge event already exists for the duplicate Station.");
                }

                if (lockedPlan.CanonicalPrimaryStationId == duplicateStationId
                    || lockedPlan.PrimaryPath.Contains(duplicateStationId))
                {
                    throw new InvalidOperationException("Station merge event would create a redirect cycle.");
                }

                var now = _clock.UtcNow;
                var aliases = lockedPlan.AliasStationIds.ToArray();
                var flattenedCount = aliases.Length == 0
                    ? 0
                    : await _db.BookingStationRedirects
                        .Where(redirect => aliases.Contains(redirect.DuplicateStationId))
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(
                                redirect => redirect.CanonicalStationId,
                                lockedPlan.CanonicalPrimaryStationId)
                            .SetProperty(redirect => redirect.UpdatedAt, now), cancellationToken);

                await _db.BookingStationRedirects.AddAsync(
                    BookingStationRedirect.Create(
                        duplicateStationId,
                        lockedPlan.CanonicalPrimaryStationId,
                        sourceEventId,
                        occurredAt),
                    cancellationToken);
                var mergeSourceIds = aliases.Append(duplicateStationId).Distinct().ToArray();
                var relinkedBookings = await _bookings.RelinkActiveStationReferencesAsync(
                    mergeSourceIds,
                    lockedPlan.CanonicalPrimaryStationId,
                    now,
                    cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return new BookingStationMergeApplicationResult(
                    true,
                    lockedPlan.CanonicalPrimaryStationId,
                    flattenedCount,
                    relinkedBookings);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                _db.ChangeTracker.Clear();
                throw;
            }
        }

        throw new InvalidOperationException(
            "Booking Station redirect graph changed during three consecutive lock-set attempts.");
    }

    private async Task<IReadOnlyList<BookingStationRedirect>> LoadGraphAsync(CancellationToken cancellationToken)
        => await _db.BookingStationRedirects
            .AsNoTracking()
            .OrderBy(redirect => redirect.DuplicateStationId)
            .ToListAsync(cancellationToken);

    private Task AcquireStationLockAsync(Guid stationId, CancellationToken cancellationToken)
        => _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended('booking-station:' || {stationId}::text, 0))",
            cancellationToken);

    private static StationMergeLockPlan BuildLockPlan(
        IReadOnlyList<BookingStationRedirect> rows,
        Guid primaryStationId,
        Guid duplicateStationId)
    {
        var graph = BookingStationRedirectGraph.ToDictionary(rows);
        var primaryPath = BookingStationRedirectGraph.ResolvePath(primaryStationId, graph);
        var duplicatePath = BookingStationRedirectGraph.ResolvePath(duplicateStationId, graph);
        var aliases = new HashSet<Guid>();
        var requiredIds = new HashSet<Guid>(primaryPath.Nodes);
        requiredIds.UnionWith(duplicatePath.Nodes);
        foreach (var redirect in rows)
        {
            var path = BookingStationRedirectGraph.ResolvePath(redirect.DuplicateStationId, graph);
            if (path.TerminalStationId != duplicateStationId)
                continue;

            aliases.Add(redirect.DuplicateStationId);
            requiredIds.UnionWith(path.Nodes);
        }

        return new StationMergeLockPlan(
            primaryPath.TerminalStationId,
            primaryPath.Nodes,
            aliases,
            requiredIds
                .OrderBy(stationId => stationId.ToString("D"), StringComparer.Ordinal)
                .ToArray());
    }

    private static void ValidateInput(Guid sourceEventId, Guid primaryStationId, Guid duplicateStationId)
    {
        if (sourceEventId == Guid.Empty)
            throw new InvalidOperationException("Station merge event id is required.");
        if (primaryStationId == Guid.Empty || duplicateStationId == Guid.Empty)
            throw new InvalidOperationException("Station merge event Station ids are required.");
        if (primaryStationId == duplicateStationId)
            throw new InvalidOperationException("Station merge event cannot redirect a Station to itself.");
    }

    private sealed record StationMergeLockPlan(
        Guid CanonicalPrimaryStationId,
        IReadOnlyList<Guid> PrimaryPath,
        IReadOnlySet<Guid> AliasStationIds,
        IReadOnlyList<Guid> RequiredLockIds);
}
