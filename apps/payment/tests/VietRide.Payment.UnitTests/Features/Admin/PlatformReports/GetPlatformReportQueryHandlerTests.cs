using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Payment.Application.Abstractions.ExternalClients;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.Admin.PlatformReports;
using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.UnitTests.Features.Admin.PlatformReports;

public sealed class GetPlatformReportQueryHandlerTests
{
    private static readonly DateTimeOffset GeneratedAt =
        new(2026, 8, 1, 0, 0, 1, TimeSpan.Zero);

    [Fact]
    public async Task Handle_MergesUnionUsesSignedRevenueSortsAndKeepsMissingName()
    {
        var operatorA = Guid.Parse("40000000-0000-0000-0000-000000000001");
        var operatorB = Guid.Parse("40000000-0000-0000-0000-000000000002");
        var operatorC = Guid.Parse("40000000-0000-0000-0000-000000000003");
        var identity = new FakeIdentityClient
        {
            Handler = (ids, _) => Task.FromResult<IReadOnlyList<OperatorSummaryItem>>(
                ids.Where(id => id != operatorC)
                    .Select(id => new OperatorSummaryItem(id, id == operatorA ? "Operator A" : "Operator B"))
                    .ToArray()),
        };
        var handler = CreateHandler(
            new FakeBookingClient
            {
                Rows =
                [
                    new(operatorA, 2, 500_000),
                    new(operatorB, 1, 200_000),
                ],
            },
            new FakeTripClient
            {
                Rows =
                [
                    new(operatorA, 1),
                    new(operatorC, 3),
                ],
            },
            new FakeParcelClient
            {
                Rows =
                [
                    new(operatorA, 1, -50_000),
                    new(operatorC, 2, 600_000),
                ],
            },
            identity);

        var result = await handler.Handle(
            ValidQuery(),
            CancellationToken.None);

        result.ByOperator.Select(item => item.OperatorId)
            .Should().Equal(operatorC, operatorA, operatorB);
        result.ByOperator[0].OperatorName.Should().BeNull();
        result.ByOperator[0].NetRevenueVnd.Should().Be(600_000);
        result.ByOperator[1].OperatorName.Should().Be("Operator A");
        result.ByOperator[1].ParcelRevenueVnd.Should().Be(-50_000);
        result.ByOperator[1].NetRevenueVnd.Should().Be(450_000);
        result.Totals.Should().Be(new PlatformReportTotals(
            CompletedBookingCount: 3,
            CompletedTripCount: 4,
            DeliveredParcelCount: 3,
            BookingRevenueVnd: 700_000,
            ParcelRevenueVnd: 550_000,
            NetRevenueVnd: 1_250_000));
        result.Period.Timezone.Should().Be("UTC");
        result.GeneratedAt.Should().Be(GeneratedAt.UtcDateTime);
    }

    [Fact]
    public async Task Handle_StartsAllMetricSourcesBeforeAwaitingAnyOne()
    {
        var allStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var startedCount = 0;

        async Task StartedAsync()
        {
            if (Interlocked.Increment(ref startedCount) == 3)
            {
                allStarted.TrySetResult();
            }

            await release.Task;
        }

        var bookings = new FakeBookingClient
        {
            Handler = async (_, _, _) =>
            {
                await StartedAsync();
                return [];
            },
        };
        var trips = new FakeTripClient
        {
            Handler = async (_, _, _) =>
            {
                await StartedAsync();
                return [];
            },
        };
        var parcels = new FakeParcelClient
        {
            Handler = async (_, _, _) =>
            {
                await StartedAsync();
                return [];
            },
        };
        var handler = CreateHandler(bookings, trips, parcels, new FakeIdentityClient());

        var reportTask = handler.Handle(ValidQuery(), CancellationToken.None);
        await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        reportTask.IsCompleted.Should().BeFalse();
        release.TrySetResult();
        await reportTask;

        startedCount.Should().Be(3);
    }

    [Fact]
    public async Task Handle_ChunksIdentityRequestsAtFiveHundred()
    {
        var ids = Enumerable.Range(1, 501)
            .Select(index => Guid.Parse($"40000000-0000-0000-0000-{index:D12}"))
            .ToArray();
        var identity = new FakeIdentityClient
        {
            Handler = (chunk, _) => Task.FromResult<IReadOnlyList<OperatorSummaryItem>>(
                chunk.Select(id => new OperatorSummaryItem(id, $"Operator {id}")).ToArray()),
        };
        var handler = CreateHandler(
            new FakeBookingClient
            {
                Rows = ids.Select(id => new BookingPlatformReportItem(id, 1, 1)).ToArray(),
            },
            new FakeTripClient(),
            new FakeParcelClient(),
            identity);

        var result = await handler.Handle(ValidQuery(), CancellationToken.None);

        result.ByOperator.Should().HaveCount(501);
        identity.Requests.Select(chunk => chunk.Count).Should().BeEquivalentTo([500, 1]);
        identity.Requests.SelectMany(chunk => chunk).Should().BeEquivalentTo(ids);
    }

    [Fact]
    public async Task Handle_WhenLocalNetOverflows_ThrowsCanonicalOverflow()
    {
        var operatorId = Guid.NewGuid();
        var handler = CreateHandler(
            new FakeBookingClient
            {
                Rows = [new BookingPlatformReportItem(operatorId, 1, long.MaxValue)],
            },
            new FakeTripClient(),
            new FakeParcelClient
            {
                Rows = [new ParcelPlatformReportItem(operatorId, 1, 1)],
            },
            new FakeIdentityClient());

        var act = () => handler.Handle(ValidQuery(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<PlatformReportValueOverflowException>();
        exception.Which.ErrorCode.Should().Be("REPORT_VALUE_OVERFLOW");
    }

    [Fact]
    public async Task Handle_WhenAnyMetricSourceFails_ReturnsNoPartialAndSkipsIdentity()
    {
        var identity = new FakeIdentityClient();
        var handler = CreateHandler(
            new FakeBookingClient
            {
                Rows = [new BookingPlatformReportItem(Guid.NewGuid(), 1, 100)],
            },
            new FakeTripClient
            {
                Handler = (_, _, _) => throw new HttpRequestException("trip unavailable"),
            },
            new FakeParcelClient(),
            identity);

        var act = () => handler.Handle(ValidQuery(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<UpstreamUnavailableException>();
        exception.Which.ErrorCode.Should().Be("UPSTREAM_UNAVAILABLE");
        identity.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenLedgerRevenueDoesNotMatchLiveSources_RejectsWholeReport()
    {
        var operatorId = Guid.Parse("40000000-0000-0000-0000-000000000004");
        var bookings = new FakeBookingClient
        {
            Rows = [new BookingPlatformReportItem(operatorId, 2, 500_000)],
        };
        var parcels = new FakeParcelClient
        {
            Rows = [new ParcelPlatformReportItem(operatorId, 1, 100_000)],
        };
        var ledger = new FakeLedgerRepository(
            [new(operatorId, 499_000, 100_000)]);
        var identity = new FakeIdentityClient();
        var handler = CreateHandler(
            bookings,
            new FakeTripClient(),
            parcels,
            identity,
            ledger);

        var act = () => handler.Handle(ValidQuery(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<UpstreamUnavailableException>();
        exception.Which.ErrorCode.Should().Be("UPSTREAM_UNAVAILABLE");
        exception.Which.StatusCode.Should().Be(503);
        identity.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenLedgerContainsUnknownRevenue_RejectsWholeReport()
    {
        var operatorId = Guid.Parse("40000000-0000-0000-0000-000000000005");
        var ledger = new FakeLedgerRepository(
            [new(operatorId, 100_000, 0)]);
        var identity = new FakeIdentityClient();
        var handler = CreateHandler(
            new FakeBookingClient(),
            new FakeTripClient(),
            new FakeParcelClient(),
            identity,
            ledger);

        var act = () => handler.Handle(ValidQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<UpstreamUnavailableException>();
        identity.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithInvalidRange_DoesNotCallSources()
    {
        var bookings = new FakeBookingClient();
        var trips = new FakeTripClient();
        var parcels = new FakeParcelClient();
        var handler = CreateHandler(bookings, trips, parcels, new FakeIdentityClient());

        var act = () => handler.Handle(
            new GetPlatformReportQuery(
                "2026-01-01T00:00:00+00:00",
                "2026-01-02T00:00:00Z"),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
        bookings.CallCount.Should().Be(0);
        trips.CallCount.Should().Be(0);
        parcels.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task LedgerSource_WithValidRange_ReturnsRepositoryRowsAndExactBounds()
    {
        var rows = new[]
        {
            new PlatformLedgerReportItem(
                Guid.Parse("40000000-0000-0000-0000-000000000006"),
                100_000,
                50_000),
        };
        var ledger = new FakeLedgerRepository(rows);
        var handler = new GetPlatformLedgerReportQueryHandler(ledger);

        var result = await handler.Handle(
            new GetPlatformLedgerReportQuery(
                "2026-07-01T00:00:00Z",
                "2026-08-01T00:00:00Z"),
            CancellationToken.None);

        result.Items.Should().BeSameAs(rows);
        ledger.CallCount.Should().Be(1);
        ledger.From.Should().Be(DateTimeOffset.Parse("2026-07-01T00:00:00Z"));
        ledger.To.Should().Be(DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
    }

    private static GetPlatformReportQueryHandler CreateHandler(
        IBookingPlatformReportClient bookings,
        ITripPlatformReportClient trips,
        IParcelPlatformReportClient parcels,
        IIdentityOperatorSummaryClient identity,
        IOperatorLedgerEntryRepository? ledger = null)
    {
        ledger ??= CreateMatchingLedger(bookings, parcels);
        return new GetPlatformReportQueryHandler(
            bookings,
            trips,
            parcels,
            identity,
            new FixedClock(GeneratedAt),
            NullLogger<GetPlatformReportQueryHandler>.Instance,
            ledger);
    }

    private static IOperatorLedgerEntryRepository CreateMatchingLedger(
        IBookingPlatformReportClient bookings,
        IParcelPlatformReportClient parcels)
    {
        var bookingRows = (bookings as FakeBookingClient)?.Rows ?? [];
        var parcelRows = (parcels as FakeParcelClient)?.Rows ?? [];
        var operatorIds = bookingRows.Select(row => row.OperatorId)
            .Concat(parcelRows.Select(row => row.OperatorId))
            .Distinct()
            .ToArray();
        var rows = operatorIds.Select(operatorId => new PlatformLedgerReportItem(
            operatorId,
            bookingRows.Where(row => row.OperatorId == operatorId)
                .Aggregate(0L, (total, row) => checked(total + row.BookingRevenueVnd)),
            parcelRows.Where(row => row.OperatorId == operatorId)
                .Aggregate(0L, (total, row) => checked(total + row.ParcelRevenueVnd))))
            .ToArray();
        return new FakeLedgerRepository(rows);
    }

    private static GetPlatformReportQuery ValidQuery()
        => new("2026-07-01T00:00:00Z", "2026-08-01T00:00:00Z");

    private sealed class FakeBookingClient : IBookingPlatformReportClient
    {
        public IReadOnlyList<BookingPlatformReportItem> Rows { get; init; } = [];
        public Func<DateTimeOffset, DateTimeOffset, CancellationToken,
            Task<IReadOnlyList<BookingPlatformReportItem>>>? Handler
        { get; init; }
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<BookingPlatformReportItem>> GetAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Handler?.Invoke(fromUtc, toUtc, cancellationToken)
                ?? Task.FromResult(Rows);
        }
    }

    private sealed class FakeTripClient : ITripPlatformReportClient
    {
        public IReadOnlyList<TripPlatformReportItem> Rows { get; init; } = [];
        public Func<DateTimeOffset, DateTimeOffset, CancellationToken,
            Task<IReadOnlyList<TripPlatformReportItem>>>? Handler
        { get; init; }
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<TripPlatformReportItem>> GetAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Handler?.Invoke(fromUtc, toUtc, cancellationToken)
                ?? Task.FromResult(Rows);
        }
    }

    private sealed class FakeParcelClient : IParcelPlatformReportClient
    {
        public IReadOnlyList<ParcelPlatformReportItem> Rows { get; init; } = [];
        public Func<DateTimeOffset, DateTimeOffset, CancellationToken,
            Task<IReadOnlyList<ParcelPlatformReportItem>>>? Handler
        { get; init; }
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<ParcelPlatformReportItem>> GetAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Handler?.Invoke(fromUtc, toUtc, cancellationToken)
                ?? Task.FromResult(Rows);
        }
    }

    private sealed class FakeIdentityClient : IIdentityOperatorSummaryClient
    {
        public Func<IReadOnlyList<Guid>, CancellationToken,
            Task<IReadOnlyList<OperatorSummaryItem>>>? Handler
        { get; init; }
        public List<IReadOnlyList<Guid>> Requests { get; } = [];

        public Task<IReadOnlyList<OperatorSummaryItem>> GetAsync(
            IReadOnlyList<Guid> operatorIds,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(operatorIds.ToArray());
            return Handler?.Invoke(operatorIds, cancellationToken)
                ?? Task.FromResult<IReadOnlyList<OperatorSummaryItem>>([]);
        }
    }

    private sealed class FakeLedgerRepository : IOperatorLedgerEntryRepository
    {
        private readonly IReadOnlyList<PlatformLedgerReportItem> _rows;

        public FakeLedgerRepository(IReadOnlyList<PlatformLedgerReportItem> rows)
        {
            _rows = rows;
        }

        public int CallCount { get; private set; }
        public DateTimeOffset From { get; private set; }
        public DateTimeOffset To { get; private set; }

        public Task<IReadOnlyList<PlatformLedgerReportItem>> GetPlatformLedgerMetricsAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken ct = default)
        {
            CallCount++;
            From = fromUtc;
            To = toUtc;
            return Task.FromResult(_rows);
        }

        public Task<OperatorLedgerEntry?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult<OperatorLedgerEntry?>(null);

        public Task<OperatorLedgerEntry> AddAsync(OperatorLedgerEntry entity, CancellationToken ct)
            => throw new NotSupportedException();

        public void Update(OperatorLedgerEntry entity) => throw new NotSupportedException();
        public void Remove(OperatorLedgerEntry entity) => throw new NotSupportedException();
        public IQueryable<OperatorLedgerEntry> Query() => Array.Empty<OperatorLedgerEntry>().AsQueryable();
        public IQueryable<OperatorLedgerEntry> QueryNoTracking() => Query();

        public Task<long> SumTripNetAmountAsync(
            Guid operatorId,
            Guid tripId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
