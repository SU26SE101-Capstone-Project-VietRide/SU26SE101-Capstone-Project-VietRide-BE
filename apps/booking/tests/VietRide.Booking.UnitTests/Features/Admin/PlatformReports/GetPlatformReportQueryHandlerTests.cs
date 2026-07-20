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
    private const string From = "2026-07-01T00:00:00Z";
    private const string To = "2026-08-01T00:00:00Z";
    private const string CacheKey = "platform-report:v1:2026-07-01T00:00:00.0000000Z:2026-08-01T00:00:00.0000000Z";

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
    public async Task Handle_CacheMiss_CallsUpstreamAndStoresExactRangeForFiveMinutes()
    {
        var cache = new FakeCache();
        var client = new FakeClient();
        var handler = CreateHandler(client, cache);

        var result = await handler.Handle(new GetPlatformReportQuery(From, To), CancellationToken.None);

        result.Totals.Should().Be(new PlatformReportTotals(1, 1, 1, 100_000, 50_000, 150_000));
        client.CallCount.Should().Be(1);
        client.From.Should().Be(DateTimeOffset.Parse(From));
        client.To.Should().Be(DateTimeOffset.Parse(To));
        cache.SetCalls.Should().ContainSingle();
        cache.SetCalls[0].Key.Should().Be(CacheKey);
        cache.SetCalls[0].Value.Should().BeSameAs(result);
        cache.SetCalls[0].Ttl.Should().Be(TimeSpan.FromMinutes(5));
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
    public async Task Handle_LedgerMismatch_ThrowsAndDoesNotPromoteCache()
    {
        var cache = new FakeCache();
        var handler = CreateHandler(new FakeClient(), cache, ledgerBookingRevenue: 99_000);

        var action = () => handler.Handle(
            new GetPlatformReportQuery(From, To),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<PlatformReportUnavailableException>();
        exception.Which.StatusCode.Should().Be(503);
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
        long ledgerBookingRevenue = 100_000)
    {
        var bookings = Substitute.For<IBookingRepository>();
        bookings.GetPlatformBookingMetricsAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<PlatformBookingReportItem>>(
                [new(OperatorId, 1, 100_000)]);
        var parcels = Substitute.For<IParcelPlatformReportClient>();
        parcels.GetAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ParcelPlatformReportItem>>(
                [new(OperatorId, 1, 50_000)]);
        var ledger = Substitute.For<IPaymentPlatformLedgerClient>();
        ledger.GetAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<PlatformLedgerReportItem>>(
                [new(OperatorId, ledgerBookingRevenue, 50_000)]);
        var identity = Substitute.For<IIdentityPlatformReportClient>();
        identity.GetAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<OperatorSummaryItem>>(
                [new(OperatorId, "Operator A")]);
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
                DateTime.Parse(From).ToUniversalTime(),
                DateTime.Parse(To).ToUniversalTime(),
                "UTC"),
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
