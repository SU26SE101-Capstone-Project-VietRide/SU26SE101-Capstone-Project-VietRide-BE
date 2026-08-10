using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Caching;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Admin.PlatformReports;
using VietRide.Booking.Application.Features.Internal.Reports.PlatformBookings;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.UnitTests.Features.Admin.PlatformReports;

public sealed class GetPlatformReportQueryHandlerTests
{
    private const string From = "2026-07-01";
    private const string To = "2026-07-31";
    private const string CacheKey = "platform-report:v3:2026-06-30T17:00:00.0000000Z:2026-07-31T17:00:00.0000000Z";

    [Fact]
    public async Task Handle_CacheHit_ReturnsCachedValueWithoutCallingUpstream()
    {
        var cached = Result();
        var cache = new FakeCache { Value = cached };
        var client = new FakeClient();
        var handler = CreateHandler(client, cache);

        var result = await handler.Handle(new GetPlatformReportQuery(From, To), CancellationToken.None);

        result.Should().BeSameAs(cached);
        client.CallCount.Should().Be(0);
        cache.GetKeys.Should().Equal(CacheKey);
        cache.SetCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CacheMiss_ConvertsVietnamInclusiveRangeAndCachesForSixtySeconds()
    {
        var cache = new FakeCache();
        var client = new FakeClient();
        var handler = CreateHandler(client, cache);

        var result = await handler.Handle(new GetPlatformReportQuery(From, To), CancellationToken.None);

        result.Totals.Should().Be(new PlatformReportTotals(1, 1, 1, 100_000, 50_000, 150_000));
        client.CallCount.Should().Be(1);
        client.From.Should().Be(DateTimeOffset.Parse("2026-06-30T17:00:00Z"));
        client.To.Should().Be(DateTimeOffset.Parse("2026-07-31T17:00:00Z"));
        cache.SetCalls.Should().ContainSingle();
        cache.SetCalls[0].Key.Should().Be(CacheKey);
        cache.SetCalls[0].Value.Should().BeSameAs(result);
        cache.SetCalls[0].Ttl.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task Handle_MalformedUpstreamResult_ThrowsAndDoesNotPromoteCache()
    {
        var cache = new FakeCache();
        var client = new FakeClient
        {
            Value = [new TripPlatformReportItem(Guid.Empty, 1)],
        };
        var handler = CreateHandler(client, cache);

        var action = () => handler.Handle(
            new GetPlatformReportQuery(From, To),
            CancellationToken.None);

        await action.Should().ThrowAsync<PlatformReportUnavailableException>();
        cache.SetCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_LedgerRevenue_IsAuthoritativeAndPromotesCache()
    {
        var cache = new FakeCache();
        var handler = CreateHandler(new FakeClient(), cache, ledgerBookingRevenue: 99_000);

        var result = await handler.Handle(
            new GetPlatformReportQuery(From, To),
            CancellationToken.None);

        result.Totals.Should().Be(new PlatformReportTotals(1, 1, 1, 99_000, 50_000, 149_000));
        cache.SetCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_SourceOnly_UsesSourceCountsAndZeroRevenue()
    {
        var cache = new FakeCache();
        var handler = CreateHandler(new FakeClient(), cache, ledgerRows: []);

        var result = await handler.Handle(
            new GetPlatformReportQuery(From, To),
            CancellationToken.None);

        result.Totals.Should().Be(new PlatformReportTotals(1, 1, 1, 0, 0, 0));
        cache.SetCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_LedgerOnlyNonzero_UsesZeroCountsAndLoadsIdentity()
    {
        var cache = new FakeCache();
        var operatorId = Guid.Parse("41000000-0000-4000-8000-000000000002");
        var identity = Substitute.For<IIdentityPlatformReportClient>();
        identity.GetAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<OperatorSummaryItem>>(
                [new OperatorSummaryItem(operatorId, "Ledger Operator")]);
        var handler = CreateHandler(
            new FakeClient { Value = [] },
            cache,
            bookingRows: [],
            parcelRows: [],
            ledgerRows: [new PlatformLedgerReportItem(operatorId, 450_000, 0)],
            identityRows: [new OperatorSummaryItem(operatorId, "Ledger Operator")],
            identityClient: identity);

        var result = await handler.Handle(
            new GetPlatformReportQuery(From, To),
            CancellationToken.None);

        result.Totals.Should().Be(new PlatformReportTotals(0, 0, 0, 450_000, 0, 450_000));
        result.ByOperator.Should().ContainSingle().Which.OperatorName.Should().Be("Ledger Operator");
        await identity.Received(1).GetAsync(
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == operatorId),
            Arg.Any<CancellationToken>());
        cache.SetCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_AllZeroLedgerOnly_OmitsRowWithoutIdentityLookup()
    {
        var cache = new FakeCache();
        var identity = Substitute.For<IIdentityPlatformReportClient>();
        var operatorId = Guid.Parse("41000000-0000-4000-8000-000000000003");
        var handler = CreateHandler(
            new FakeClient { Value = [] },
            cache,
            bookingRows: [],
            parcelRows: [],
            ledgerRows: [new PlatformLedgerReportItem(operatorId, 0, 0)],
            identityRows: [],
            identityClient: identity);

        var result = await handler.Handle(
            new GetPlatformReportQuery(From, To),
            CancellationToken.None);

        result.ByOperator.Should().BeEmpty();
        await identity.DidNotReceiveWithAnyArgs().GetAsync(default!, default);
        cache.SetCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_LedgerOnlyWithoutIdentitySummary_ReturnsNullOperatorName()
    {
        var cache = new FakeCache();
        var operatorId = Guid.Parse("41000000-0000-4000-8000-000000000004");
        var handler = CreateHandler(
            new FakeClient { Value = [] },
            cache,
            bookingRows: [],
            parcelRows: [],
            ledgerRows: [new PlatformLedgerReportItem(operatorId, 450_000, 0)],
            identityRows: []);

        var result = await handler.Handle(
            new GetPlatformReportQuery(From, To),
            CancellationToken.None);

        result.ByOperator.Should().ContainSingle().Which.OperatorName.Should().BeNull();
        cache.SetCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_NegativeLedgerRevenue_IsAcceptedAndUsesCheckedNetSum()
    {
        var cache = new FakeCache();
        var operatorId = Guid.Parse("41000000-0000-4000-8000-000000000005");
        var handler = CreateHandler(
            new FakeClient { Value = [] },
            cache,
            bookingRows: [],
            parcelRows: [],
            ledgerRows: [new PlatformLedgerReportItem(operatorId, -450_000, 50_000)],
            identityRows: []);

        var result = await handler.Handle(
            new GetPlatformReportQuery(From, To),
            CancellationToken.None);

        result.Totals.Should().Be(new PlatformReportTotals(0, 0, 0, -450_000, 50_000, -400_000));
        cache.SetCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_LedgerOnlyRevenueSumOverflow_ThrowsAndDoesNotPromoteCache()
    {
        var cache = new FakeCache();
        var operatorId = Guid.Parse("41000000-0000-4000-8000-000000000006");
        var handler = CreateHandler(
            new FakeClient { Value = [] },
            cache,
            bookingRows: [],
            parcelRows: [],
            ledgerRows: [new PlatformLedgerReportItem(operatorId, long.MaxValue, 1)],
            identityRows: []);

        var action = () => handler.Handle(
            new GetPlatformReportQuery(From, To),
            CancellationToken.None);

        await action.Should().ThrowAsync<PlatformReportValueOverflowException>();
        cache.SetCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_DuplicateLedgerRows_ThrowsAndDoesNotPromoteCache()
    {
        var cache = new FakeCache();
        var handler = CreateHandler(
            new FakeClient(),
            cache,
            ledgerRows:
            [
                new PlatformLedgerReportItem(OperatorId, 100_000, 0),
                new PlatformLedgerReportItem(OperatorId, 100_000, 0),
            ]);

        var action = () => handler.Handle(
            new GetPlatformReportQuery(From, To),
            CancellationToken.None);

        await action.Should().ThrowAsync<PlatformReportUnavailableException>();
        cache.SetCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_EmptyLedgerOperatorId_ThrowsAndDoesNotPromoteCache()
    {
        var cache = new FakeCache();
        var handler = CreateHandler(
            new FakeClient(),
            cache,
            ledgerRows: [new PlatformLedgerReportItem(Guid.Empty, 0, 0)]);

        var action = () => handler.Handle(
            new GetPlatformReportQuery(From, To),
            CancellationToken.None);

        await action.Should().ThrowAsync<PlatformReportUnavailableException>();
        cache.SetCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_MalformedCache_FallsBackToSourcesAndOverwritesEntry()
    {
        var malformed = Result(
            [new PlatformReportOperatorItem(Guid.Empty, null, 1, 1, 1, 100_000, 50_000, 150_000)]);
        var cache = new FakeCache { Value = malformed };
        var client = new FakeClient();
        var handler = CreateHandler(client, cache);

        var result = await handler.Handle(
            new GetPlatformReportQuery(From, To),
            CancellationToken.None);

        result.Should().NotBeSameAs(malformed);
        client.CallCount.Should().Be(1);
        cache.SetCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_ConcurrentMissesForSameRange_UseSingleUpstreamRequest()
    {
        var release = new TaskCompletionSource<IReadOnlyList<TripPlatformReportItem>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new FakeCache();
        var client = new FakeClient { Handler = _ => release.Task };
        var handler = CreateHandler(client, cache);

        var first = handler.Handle(new GetPlatformReportQuery(From, To), CancellationToken.None);
        var second = handler.Handle(new GetPlatformReportQuery(From, To), CancellationToken.None);
        await WaitUntilAsync(() => client.CallCount == 1);
        release.SetResult([new TripPlatformReportItem(OperatorId, 1)]);

        var results = await Task.WhenAll(first, second);

        results[0].Should().BeSameAs(results[1]);
        client.CallCount.Should().Be(1);
        cache.SetCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_StaleCache_FallsBackToReconciledSources()
    {
        var stale = Result() with
        {
            GeneratedAt = DateTime.Parse("2026-07-31T23:54:59Z").ToUniversalTime(),
        };
        var cache = new FakeCache { Value = stale };
        var client = new FakeClient();
        var handler = CreateHandler(client, cache);

        var result = await handler.Handle(
            new GetPlatformReportQuery(From, To),
            CancellationToken.None);

        result.Should().NotBeSameAs(stale);
        client.CallCount.Should().Be(1);
        cache.SetCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_ConcurrentMissesWithCacheUnavailable_StillUseSingleUpstreamRequest()
    {
        var release = new TaskCompletionSource<IReadOnlyList<TripPlatformReportItem>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new FakeCache { ThrowOnAccess = true };
        var client = new FakeClient { Handler = _ => release.Task };
        var handler = CreateHandler(client, cache);

        var first = handler.Handle(new GetPlatformReportQuery(From, To), CancellationToken.None);
        var second = handler.Handle(new GetPlatformReportQuery(From, To), CancellationToken.None);
        await WaitUntilAsync(() => client.CallCount == 1);
        release.SetResult([new TripPlatformReportItem(OperatorId, 1)]);

        var results = await Task.WhenAll(first, second);

        results[0].Should().BeSameAs(results[1]);
        client.CallCount.Should().Be(1);
    }

    private static GetPlatformReportQueryHandler CreateHandler(
        ITripPlatformReportClient client,
        IPlatformReportCache cache,
        long ledgerBookingRevenue = 100_000,
        IReadOnlyList<PlatformBookingReportItem>? bookingRows = null,
        IReadOnlyList<ParcelPlatformReportItem>? parcelRows = null,
        IReadOnlyList<PlatformLedgerReportItem>? ledgerRows = null,
        IReadOnlyList<OperatorSummaryItem>? identityRows = null,
        IIdentityPlatformReportClient? identityClient = null)
    {
        var bookings = Substitute.For<IBookingRepository>();
        bookings.GetPlatformBookingMetricsAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<PlatformBookingReportItem>>(
                bookingRows ?? [new(OperatorId, 1, 100_000)]);
        var parcels = Substitute.For<IParcelPlatformReportClient>();
        parcels.GetAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ParcelPlatformReportItem>>(
                parcelRows ?? [new(OperatorId, 1)]);
        var ledger = Substitute.For<IPaymentPlatformLedgerClient>();
        ledger.GetAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<PlatformLedgerReportItem>>(
                ledgerRows ?? [new(OperatorId, ledgerBookingRevenue, 50_000)]);
        var identity = identityClient ?? Substitute.For<IIdentityPlatformReportClient>();
        identity.GetAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<OperatorSummaryItem>>(
                identityRows ?? [new(OperatorId, "Operator A")]);
        return new GetPlatformReportQueryHandler(
            bookings,
            client,
            parcels,
            ledger,
            identity,
            cache,
            new FixedClock(),
            NullLogger<GetPlatformReportQueryHandler>.Instance);
    }

    private static readonly Guid OperatorId =
        Guid.Parse("41000000-0000-4000-8000-000000000001");

    private static PlatformReportResult Result(
        IReadOnlyList<PlatformReportOperatorItem>? operators = null)
        => new(
            new PlatformReportPeriod(
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 31),
                "Asia/Ho_Chi_Minh"),
            new PlatformReportTotals(1, 1, 1, 100_000, 50_000, 150_000),
            operators ??
            [
                new PlatformReportOperatorItem(
                    OperatorId,
                    "Operator A",
                    1,
                    1,
                    1,
                    100_000,
                    50_000,
                    150_000),
            ],
            DateTime.Parse("2026-08-01T00:00:01Z").ToUniversalTime());

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        condition().Should().BeTrue();
    }

    private sealed class FakeClient : ITripPlatformReportClient
    {
        private int _callCount;

        public IReadOnlyList<TripPlatformReportItem> Value { get; init; } =
            [new TripPlatformReportItem(OperatorId, 1)];
        public Func<CancellationToken, Task<IReadOnlyList<TripPlatformReportItem>>>? Handler { get; init; }
        public int CallCount => Volatile.Read(ref _callCount);
        public DateTimeOffset From { get; private set; }
        public DateTimeOffset To { get; private set; }

        public Task<IReadOnlyList<TripPlatformReportItem>> GetAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _callCount);
            From = fromUtc;
            To = toUtc;
            return Handler?.Invoke(ct) ?? Task.FromResult(Value);
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } =
            DateTimeOffset.Parse("2026-08-01T00:00:01Z");
    }

    private sealed class FakeCache : IPlatformReportCache
    {
        private readonly object _sync = new();
        private PlatformReportResult? _value;

        public PlatformReportResult? Value
        {
            get
            {
                lock (_sync) return _value;
            }
            init => _value = value;
        }

        public List<string> GetKeys { get; } = [];
        public List<SetCall> SetCalls { get; } = [];
        public bool ThrowOnAccess { get; init; }

        public Task<PlatformReportResult?> GetAsync(
            string key,
            CancellationToken ct = default)
        {
            if (ThrowOnAccess)
                throw new InvalidOperationException("cache unavailable");

            lock (_sync)
            {
                GetKeys.Add(key);
                return Task.FromResult(_value);
            }
        }

        public Task SetAsync(
            string key,
            PlatformReportResult value,
            TimeSpan ttl,
            CancellationToken ct = default)
        {
            if (ThrowOnAccess)
                throw new InvalidOperationException("cache unavailable");

            lock (_sync)
            {
                _value = value;
                SetCalls.Add(new SetCall(key, value, ttl));
            }

            return Task.CompletedTask;
        }
    }

    private sealed record SetCall(
        string Key,
        PlatformReportResult Value,
        TimeSpan Ttl);
}
