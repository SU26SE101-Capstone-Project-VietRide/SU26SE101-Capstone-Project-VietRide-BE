using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Application.Features.Admin.PlatformReports;

public sealed class GetPlatformReportQueryHandler
    : IRequestHandler<GetPlatformReportQuery, PlatformReportResult>
{
    private const int IdentityChunkSize = 500;

    private readonly IBookingPlatformReportClient _bookings;
    private readonly ITripPlatformReportClient _trips;
    private readonly IParcelPlatformReportClient _parcels;
    private readonly IIdentityOperatorSummaryClient _identity;
    private readonly IClock _clock;
    private readonly ILogger<GetPlatformReportQueryHandler> _logger;
    private readonly IOperatorLedgerEntryRepository _ledger;

    public GetPlatformReportQueryHandler(
        IBookingPlatformReportClient bookings,
        ITripPlatformReportClient trips,
        IParcelPlatformReportClient parcels,
        IIdentityOperatorSummaryClient identity,
        IClock clock,
        ILogger<GetPlatformReportQueryHandler> logger,
        IOperatorLedgerEntryRepository ledger)
    {
        _bookings = bookings;
        _trips = trips;
        _parcels = parcels;
        _identity = identity;
        _clock = clock;
        _logger = logger;
        _ledger = ledger;
    }

    public async Task<PlatformReportResult> Handle(
        GetPlatformReportQuery request,
        CancellationToken cancellationToken)
    {
        var range = PlatformReportUtcRange.Parse(request.From, request.To);
        Task<IReadOnlyList<BookingPlatformReportItem>> bookingTask;
        Task<IReadOnlyList<TripPlatformReportItem>> tripTask;
        Task<IReadOnlyList<ParcelPlatformReportItem>> parcelTask;
        Task<IReadOnlyList<PlatformLedgerReportItem>> ledgerTask;

        try
        {
            bookingTask = _bookings.GetAsync(range.From, range.To, cancellationToken);
            tripTask = _trips.GetAsync(range.From, range.To, cancellationToken);
            parcelTask = _parcels.GetAsync(range.From, range.To, cancellationToken);
            ledgerTask = _ledger.GetPlatformLedgerMetricsAsync(
                range.From,
                range.To,
                cancellationToken);
            await Task.WhenAll(bookingTask, tripTask, parcelTask, ledgerTask);
        }
        catch (PlatformReportValueOverflowException)
        {
            throw;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpstreamUnavailableException(exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new UpstreamUnavailableException(exception);
        }

        try
        {
            var accumulators = BuildAccumulators(
                await bookingTask,
                await tripTask,
                await parcelTask);
            ReconcileLedgerRevenue(accumulators, await ledgerTask);
            var names = await LoadOperatorNamesAsync(accumulators.Keys, cancellationToken);
            var byOperator = accumulators.Values
                .Select(item => item.ToResult(names.GetValueOrDefault(item.OperatorId)))
                .OrderByDescending(item => item.NetRevenueVnd)
                .ThenBy(item => item.OperatorId)
                .ToArray();
            var totals = SumTotals(byOperator);

            foreach (var item in byOperator.Where(item => item.OperatorName is null))
            {
                _logger.LogWarning(
                    "Platform report operator summary missing for {OperatorId}",
                    item.OperatorId);
            }

            return new PlatformReportResult(
                new PlatformReportPeriod(range.From.UtcDateTime, range.To.UtcDateTime, "UTC"),
                totals,
                byOperator,
                _clock.UtcNow.UtcDateTime);
        }
        catch (PlatformReportValueOverflowException)
        {
            throw;
        }
        catch (UpstreamUnavailableException)
        {
            throw;
        }
        catch (OverflowException exception)
        {
            throw new PlatformReportValueOverflowException(exception);
        }
    }

    private async Task<IReadOnlyDictionary<Guid, string>> LoadOperatorNamesAsync(
        IEnumerable<Guid> operatorIds,
        CancellationToken cancellationToken)
    {
        var ids = operatorIds.Order().ToArray();
        var tasks = ids.Chunk(IdentityChunkSize)
            .Select(chunk => _identity.GetAsync(chunk, cancellationToken))
            .ToArray();
        try
        {
            var responses = await Task.WhenAll(tasks);
            var requested = ids.ToHashSet();
            var names = new Dictionary<Guid, string>();
            foreach (var summary in responses.SelectMany(response => response))
            {
                if (summary.OperatorId == Guid.Empty
                    || !requested.Contains(summary.OperatorId)
                    || string.IsNullOrWhiteSpace(summary.OperatorName)
                    || !names.TryAdd(summary.OperatorId, summary.OperatorName))
                {
                    throw new UpstreamUnavailableException();
                }
            }

            return names;
        }
        catch (UpstreamUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpstreamUnavailableException(exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new UpstreamUnavailableException(exception);
        }
    }

    private static Dictionary<Guid, OperatorAccumulator> BuildAccumulators(
        IReadOnlyList<BookingPlatformReportItem> bookings,
        IReadOnlyList<TripPlatformReportItem> trips,
        IReadOnlyList<ParcelPlatformReportItem> parcels)
    {
        var result = new Dictionary<Guid, OperatorAccumulator>();
        foreach (var item in bookings)
        {
            Validate(item.OperatorId, item.CompletedBookingCount);
            Get(result, item.OperatorId).AddBooking(
                item.CompletedBookingCount,
                item.BookingRevenueVnd);
        }

        foreach (var item in trips)
        {
            Validate(item.OperatorId, item.CompletedTripCount);
            Get(result, item.OperatorId).AddTrip(item.CompletedTripCount);
        }

        foreach (var item in parcels)
        {
            Validate(item.OperatorId, item.DeliveredParcelCount);
            Get(result, item.OperatorId).AddParcel(
                item.DeliveredParcelCount,
                item.ParcelRevenueVnd);
        }

        return result;
    }

    private static void Validate(Guid operatorId, long count)
    {
        if (operatorId == Guid.Empty || count < 0)
        {
            throw new UpstreamUnavailableException();
        }
    }

    private void ReconcileLedgerRevenue(
        IDictionary<Guid, OperatorAccumulator> accumulators,
        IReadOnlyList<PlatformLedgerReportItem> ledgerRows)
    {
        var ledgerByOperator = new Dictionary<Guid, PlatformLedgerReportItem>();
        foreach (var row in ledgerRows)
        {
            if (row.OperatorId == Guid.Empty
                || !ledgerByOperator.TryAdd(row.OperatorId, row))
            {
                throw new UpstreamUnavailableException();
            }
        }

        foreach (var accumulator in accumulators.Values)
        {
            ledgerByOperator.TryGetValue(accumulator.OperatorId, out var ledger);
            var ledgerBookingRevenue = ledger?.BookingRevenueVnd ?? 0;
            var ledgerParcelRevenue = ledger?.ParcelRevenueVnd ?? 0;
            if (accumulator.BookingRevenueVnd != ledgerBookingRevenue
                || accumulator.ParcelRevenueVnd != ledgerParcelRevenue)
            {
                LogReconciliationMismatch(
                    accumulator.OperatorId,
                    accumulator.BookingRevenueVnd,
                    ledgerBookingRevenue,
                    accumulator.ParcelRevenueVnd,
                    ledgerParcelRevenue);
                throw new UpstreamUnavailableException();
            }

            ledgerByOperator.Remove(accumulator.OperatorId);
        }

        foreach (var ledger in ledgerByOperator.Values)
        {
            if (ledger.BookingRevenueVnd == 0 && ledger.ParcelRevenueVnd == 0)
            {
                continue;
            }

            LogReconciliationMismatch(
                ledger.OperatorId,
                0,
                ledger.BookingRevenueVnd,
                0,
                ledger.ParcelRevenueVnd);
            throw new UpstreamUnavailableException();
        }
    }

    private void LogReconciliationMismatch(
        Guid operatorId,
        long liveBookingRevenue,
        long ledgerBookingRevenue,
        long liveParcelRevenue,
        long ledgerParcelRevenue)
        => _logger.LogError(
            "Platform report reconciliation mismatch for {OperatorId}: "
            + "live booking {LiveBookingRevenueVnd}, ledger booking {LedgerBookingRevenueVnd}, "
            + "live parcel {LiveParcelRevenueVnd}, ledger parcel {LedgerParcelRevenueVnd}",
            operatorId,
            liveBookingRevenue,
            ledgerBookingRevenue,
            liveParcelRevenue,
            ledgerParcelRevenue);

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

    private static PlatformReportTotals SumTotals(
        IReadOnlyList<PlatformReportOperatorItem> items)
    {
        long completedBookingCount = 0;
        long completedTripCount = 0;
        long deliveredParcelCount = 0;
        long bookingRevenueVnd = 0;
        long parcelRevenueVnd = 0;
        long netRevenueVnd = 0;
        foreach (var item in items)
        {
            completedBookingCount = checked(completedBookingCount + item.CompletedBookingCount);
            completedTripCount = checked(completedTripCount + item.CompletedTripCount);
            deliveredParcelCount = checked(deliveredParcelCount + item.DeliveredParcelCount);
            bookingRevenueVnd = checked(bookingRevenueVnd + item.BookingRevenueVnd);
            parcelRevenueVnd = checked(parcelRevenueVnd + item.ParcelRevenueVnd);
            netRevenueVnd = checked(netRevenueVnd + item.NetRevenueVnd);
        }

        return new PlatformReportTotals(
            completedBookingCount,
            completedTripCount,
            deliveredParcelCount,
            bookingRevenueVnd,
            parcelRevenueVnd,
            netRevenueVnd);
    }

    private sealed class OperatorAccumulator
    {
        public OperatorAccumulator(Guid operatorId)
        {
            OperatorId = operatorId;
        }

        public Guid OperatorId { get; }
        public long CompletedBookingCount { get; private set; }
        public long CompletedTripCount { get; private set; }
        public long DeliveredParcelCount { get; private set; }
        public long BookingRevenueVnd { get; private set; }
        public long ParcelRevenueVnd { get; private set; }

        public void AddBooking(long count, long revenue)
        {
            CompletedBookingCount = checked(CompletedBookingCount + count);
            BookingRevenueVnd = checked(BookingRevenueVnd + revenue);
        }

        public void AddTrip(long count)
            => CompletedTripCount = checked(CompletedTripCount + count);

        public void AddParcel(long count, long revenue)
        {
            DeliveredParcelCount = checked(DeliveredParcelCount + count);
            ParcelRevenueVnd = checked(ParcelRevenueVnd + revenue);
        }

        public PlatformReportOperatorItem ToResult(string? operatorName)
            => new(
                OperatorId,
                operatorName,
                CompletedBookingCount,
                CompletedTripCount,
                DeliveredParcelCount,
                BookingRevenueVnd,
                ParcelRevenueVnd,
                checked(BookingRevenueVnd + ParcelRevenueVnd));
    }
}
