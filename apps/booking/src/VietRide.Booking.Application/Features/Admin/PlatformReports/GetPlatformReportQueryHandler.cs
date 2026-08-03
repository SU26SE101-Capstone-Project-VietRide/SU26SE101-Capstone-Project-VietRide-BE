using System.Collections.Concurrent;
using System.Globalization;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.Caching;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Internal.Reports.PlatformBookings;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Application.Features.Admin.PlatformReports;

public sealed class GetPlatformReportQueryHandler
    : IRequestHandler<GetPlatformReportQuery, PlatformReportResult>
{
    private const int IdentityChunkSize = 500;
    private static readonly ConcurrentDictionary<string, InFlightGate> InFlight = new(StringComparer.Ordinal);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IBookingRepository _bookings;
    private readonly ITripPlatformReportClient _trips;
    private readonly IParcelPlatformReportClient _parcels;
    private readonly IPaymentPlatformLedgerClient _ledger;
    private readonly IIdentityPlatformReportClient _identity;
    private readonly IPlatformReportCache _cache;
    private readonly IClock _clock;
    private readonly ILogger<GetPlatformReportQueryHandler> _logger;

    public GetPlatformReportQueryHandler(
        IBookingRepository bookings,
        ITripPlatformReportClient trips,
        IParcelPlatformReportClient parcels,
        IPaymentPlatformLedgerClient ledger,
        IIdentityPlatformReportClient identity,
        IPlatformReportCache cache,
        IClock clock,
        ILogger<GetPlatformReportQueryHandler> logger)
    {
        _bookings = bookings;
        _trips = trips;
        _parcels = parcels;
        _ledger = ledger;
        _identity = identity;
        _cache = cache;
        _clock = clock;
        _logger = logger;
    }

    public async Task<PlatformReportResult> Handle(GetPlatformReportQuery request, CancellationToken ct)
    {
        var range = PlatformReportUtcRange.Parse(request.From, request.To);
        var key = BuildCacheKey(range.From, range.To);
        var cached = await TryGetCacheAsync(key, ct).ConfigureAwait(false);
        if (TryValidateResult(cached, range))
        {
            return cached!;
        }

        var gate = RentGate(key);
        var acquired = false;
        try
        {
            await gate.Semaphore.WaitAsync(ct).ConfigureAwait(false);
            acquired = true;
            if (TryValidateResult(gate.Result, range))
            {
                return gate.Result!;
            }

            cached = await TryGetCacheAsync(key, ct).ConfigureAwait(false);
            if (TryValidateResult(cached, range))
            {
                gate.Result = cached;
                return cached!;
            }

            var result = await BuildReportAsync(range, ct).ConfigureAwait(false);
            gate.Result = result;
            await TrySetCacheAsync(key, result, ct).ConfigureAwait(false);
            return result;
        }
        finally
        {
            if (acquired)
            {
                gate.Semaphore.Release();
            }

            if (gate.ReleaseReference() == 0)
            {
                InFlight.TryRemove(new KeyValuePair<string, InFlightGate>(key, gate));
            }
        }
    }

    private static InFlightGate RentGate(string key)
    {
        while (true)
        {
            var gate = InFlight.GetOrAdd(key, static _ => new InFlightGate());
            gate.AddReference();
            if (InFlight.TryGetValue(key, out var current)
                && ReferenceEquals(gate, current))
            {
                return gate;
            }

            gate.ReleaseReference();
        }
    }

    private async Task<PlatformReportResult> BuildReportAsync(
        PlatformReportUtcRange range,
        CancellationToken ct)
    {
        Task<IReadOnlyList<PlatformBookingReportItem>> bookingTask;
        Task<IReadOnlyList<TripPlatformReportItem>> tripTask;
        Task<IReadOnlyList<ParcelPlatformReportItem>> parcelTask;
        Task<IReadOnlyList<PlatformLedgerReportItem>> ledgerTask;
        try
        {
            bookingTask = _bookings.GetPlatformBookingMetricsAsync(range.From, range.To, ct);
            tripTask = _trips.GetAsync(range.From, range.To, ct);
            parcelTask = _parcels.GetAsync(range.From, range.To, ct);
            ledgerTask = _ledger.GetAsync(range.From, range.To, ct);
            await Task.WhenAll(bookingTask, tripTask, parcelTask, ledgerTask).ConfigureAwait(false);
        }
        catch (PlatformReportValueOverflowException)
        {
            throw;
        }
        catch (OperationCanceledException exception) when (!ct.IsCancellationRequested)
        {
            throw new PlatformReportUnavailableException(exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new PlatformReportUnavailableException(exception);
        }

        try
        {
            var accumulators = BuildAccumulators(
                await bookingTask.ConfigureAwait(false),
                await tripTask.ConfigureAwait(false),
                await parcelTask.ConfigureAwait(false));
            var ledgerByOperator = ApplyLedgerRevenue(
                accumulators,
                await ledgerTask.ConfigureAwait(false));
            var names = await LoadOperatorNamesAsync(accumulators.Keys, ct).ConfigureAwait(false);
            var byOperator = accumulators.Values
                .Select(item =>
                {
                    var ledger = ledgerByOperator.GetValueOrDefault(item.OperatorId);
                    return item.ToResult(
                        names.GetValueOrDefault(item.OperatorId),
                        ledger?.BookingRevenueVnd ?? 0,
                        ledger?.ParcelRevenueVnd ?? 0);
                })
                .OrderByDescending(item => item.NetRevenueVnd)
                .ThenBy(item => item.OperatorId)
                .ToArray();
            var result = new PlatformReportResult(
                new PlatformReportPeriod(range.From.UtcDateTime, range.To.UtcDateTime, "UTC"),
                SumTotals(byOperator),
                byOperator,
                _clock.UtcNow.UtcDateTime);
            ValidateResult(result, range);
            return result;
        }
        catch (PlatformReportValueOverflowException)
        {
            throw;
        }
        catch (PlatformReportUnavailableException)
        {
            throw;
        }
        catch (OverflowException exception)
        {
            throw new PlatformReportValueOverflowException(exception);
        }
    }

    private async Task<PlatformReportResult?> TryGetCacheAsync(string key, CancellationToken ct)
    {
        try
        {
            return await _cache.GetAsync(key, ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Platform report cache read failed for key {CacheKey}.", key);
            return null;
        }
    }

    private async Task TrySetCacheAsync(string key, PlatformReportResult value, CancellationToken ct)
    {
        try
        {
            await _cache.SetAsync(key, value, CacheTtl, ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Platform report cache write failed for key {CacheKey}.", key);
        }
    }

    private bool TryValidateResult(
        PlatformReportResult? result,
        PlatformReportUtcRange range)
    {
        if (result is null)
        {
            return false;
        }

        try
        {
            ValidateResult(result, range);
            return true;
        }
        catch (Exception exception) when (exception is PlatformReportUnavailableException or OverflowException)
        {
            _logger.LogWarning(exception, "Ignoring malformed platform report cache entry.");
            return false;
        }
    }

    private void ValidateResult(
        PlatformReportResult result,
        PlatformReportUtcRange range)
    {
        var nowUtc = _clock.UtcNow.UtcDateTime;
        if (result.ByOperator is null
            || result.Totals is null
            || result.Period is null
            || result.Period.From != range.From.UtcDateTime
            || result.Period.To != range.To.UtcDateTime
            || result.Period.From.Kind != DateTimeKind.Utc
            || result.Period.To.Kind != DateTimeKind.Utc
            || !string.Equals(result.Period.Timezone, "UTC", StringComparison.Ordinal)
            || result.GeneratedAt.Kind != DateTimeKind.Utc
            || result.GeneratedAt < nowUtc.Subtract(CacheTtl)
            || result.GeneratedAt > nowUtc.AddMinutes(1))
        {
            throw new PlatformReportUnavailableException();
        }

        var seen = new HashSet<Guid>();
        foreach (var row in result.ByOperator)
        {
            if (row.OperatorId == Guid.Empty
                || row.CompletedBookingCount < 0
                || row.CompletedTripCount < 0
                || row.DeliveredParcelCount < 0
                || row.NetRevenueVnd != checked(row.BookingRevenueVnd + row.ParcelRevenueVnd)
                || !seen.Add(row.OperatorId))
            {
                throw new PlatformReportUnavailableException();
            }
        }

        if (result.Totals != SumTotals(result.ByOperator))
        {
            throw new PlatformReportUnavailableException();
        }
    }

    private static string BuildCacheKey(DateTimeOffset fromUtc, DateTimeOffset toUtc)
        => $"platform-report:v2:{fromUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)}:{toUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)}";

    private async Task<IReadOnlyDictionary<Guid, string>> LoadOperatorNamesAsync(
        IEnumerable<Guid> operatorIds,
        CancellationToken ct)
    {
        var ids = operatorIds.Order().ToArray();
        var tasks = ids.Chunk(IdentityChunkSize)
            .Select(chunk => _identity.GetAsync(chunk, ct))
            .ToArray();
        try
        {
            var responses = await Task.WhenAll(tasks).ConfigureAwait(false);
            var requested = ids.ToHashSet();
            var names = new Dictionary<Guid, string>();
            foreach (var summary in responses.SelectMany(response => response))
            {
                if (summary.OperatorId == Guid.Empty
                    || !requested.Contains(summary.OperatorId)
                    || string.IsNullOrWhiteSpace(summary.OperatorName)
                    || !names.TryAdd(summary.OperatorId, summary.OperatorName))
                {
                    throw new PlatformReportUnavailableException();
                }
            }

            return names;
        }
        catch (PlatformReportUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException exception) when (!ct.IsCancellationRequested)
        {
            throw new PlatformReportUnavailableException(exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new PlatformReportUnavailableException(exception);
        }
    }

    private static Dictionary<Guid, OperatorAccumulator> BuildAccumulators(
        IReadOnlyList<PlatformBookingReportItem> bookings,
        IReadOnlyList<TripPlatformReportItem> trips,
        IReadOnlyList<ParcelPlatformReportItem> parcels)
    {
        var result = new Dictionary<Guid, OperatorAccumulator>();
        AddUnique(
            bookings,
            item => item.OperatorId,
            item => item.CompletedBookingCount,
            item => Get(result, item.OperatorId).AddBooking(item.CompletedBookingCount));
        AddUnique(
            trips,
            item => item.OperatorId,
            item => item.CompletedTripCount,
            item => Get(result, item.OperatorId).AddTrip(item.CompletedTripCount));
        AddUnique(
            parcels,
            item => item.OperatorId,
            item => item.DeliveredParcelCount,
            item => Get(result, item.OperatorId).AddParcel(item.DeliveredParcelCount));
        return result;
    }

    private static void AddUnique<T>(
        IEnumerable<T> items,
        Func<T, Guid> operatorId,
        Func<T, long> count,
        Action<T> add)
    {
        var seen = new HashSet<Guid>();
        foreach (var item in items)
        {
            var id = operatorId(item);
            if (id == Guid.Empty || count(item) < 0 || !seen.Add(id))
            {
                throw new PlatformReportUnavailableException();
            }

            add(item);
        }
    }

    private static Dictionary<Guid, PlatformLedgerReportItem> ApplyLedgerRevenue(
        IDictionary<Guid, OperatorAccumulator> accumulators,
        IReadOnlyList<PlatformLedgerReportItem> ledgerRows)
    {
        var ledgerByOperator = new Dictionary<Guid, PlatformLedgerReportItem>();
        foreach (var row in ledgerRows)
        {
            if (row.OperatorId == Guid.Empty || !ledgerByOperator.TryAdd(row.OperatorId, row))
            {
                throw new PlatformReportUnavailableException();
            }
        }

        foreach (var ledger in ledgerByOperator.Values)
        {
            if (ledger.BookingRevenueVnd == 0 && ledger.ParcelRevenueVnd == 0)
            {
                continue;
            }

            Get(accumulators, ledger.OperatorId);
        }

        return ledgerByOperator;
    }

    private static OperatorAccumulator Get(
        IDictionary<Guid, OperatorAccumulator> accumulators,
        Guid operatorId)
    {
        if (!accumulators.TryGetValue(operatorId, out var accumulator))
        {
            accumulator = new OperatorAccumulator(operatorId);
            accumulators.Add(operatorId, accumulator);
        }

        return accumulator;
    }

    private static PlatformReportTotals SumTotals(IReadOnlyList<PlatformReportOperatorItem> items)
    {
        long completedBookings = 0;
        long completedTrips = 0;
        long deliveredParcels = 0;
        long bookingRevenue = 0;
        long parcelRevenue = 0;
        long netRevenue = 0;
        foreach (var item in items)
        {
            completedBookings = checked(completedBookings + item.CompletedBookingCount);
            completedTrips = checked(completedTrips + item.CompletedTripCount);
            deliveredParcels = checked(deliveredParcels + item.DeliveredParcelCount);
            bookingRevenue = checked(bookingRevenue + item.BookingRevenueVnd);
            parcelRevenue = checked(parcelRevenue + item.ParcelRevenueVnd);
            netRevenue = checked(netRevenue + item.NetRevenueVnd);
        }

        return new PlatformReportTotals(
            completedBookings,
            completedTrips,
            deliveredParcels,
            bookingRevenue,
            parcelRevenue,
            netRevenue);
    }

    private sealed class OperatorAccumulator
    {
        public OperatorAccumulator(Guid operatorId) => OperatorId = operatorId;

        public Guid OperatorId { get; }
        public long CompletedBookingCount { get; private set; }
        public long CompletedTripCount { get; private set; }
        public long DeliveredParcelCount { get; private set; }

        public void AddBooking(long count)
            => CompletedBookingCount = checked(CompletedBookingCount + count);

        public void AddTrip(long count)
            => CompletedTripCount = checked(CompletedTripCount + count);

        public void AddParcel(long count)
            => DeliveredParcelCount = checked(DeliveredParcelCount + count);

        public PlatformReportOperatorItem ToResult(
            string? operatorName,
            long bookingRevenueVnd,
            long parcelRevenueVnd)
            => new(
                OperatorId,
                operatorName,
                CompletedBookingCount,
                CompletedTripCount,
                DeliveredParcelCount,
                bookingRevenueVnd,
                parcelRevenueVnd,
                checked(bookingRevenueVnd + parcelRevenueVnd));
    }

    private sealed class InFlightGate
    {
        private int _references;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public PlatformReportResult? Result { get; set; }

        public void AddReference() => Interlocked.Increment(ref _references);

        public int ReleaseReference() => Interlocked.Decrement(ref _references);
    }
}
