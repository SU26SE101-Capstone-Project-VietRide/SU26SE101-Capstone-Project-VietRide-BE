using FluentAssertions;
using VietRide.Payment.Application.Features.RevenueAnalytics.Admin;
using VietRide.Payment.Infrastructure.Caching;

namespace VietRide.Payment.UnitTests.Features.RevenueAnalytics;

public sealed class RevenueAnalyticsCacheTests
{
    [Fact]
    public async Task RedisCache_RoundTripsResponseAndWritesExactSixtySecondTtl()
    {
        var store = new FakeRevenueCacheStore();
        var cache = new RedisRevenueReportCache(store);
        var expected = new AdminRevenueAnalyticsResponse(
            new AdminRevenuePeriod(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), "Asia/Ho_Chi_Minh"),
            new AdminRevenueSummary(
                new AdminRevenueComparisons(
                    Comparison(100), Comparison(90), Comparison(60), Comparison(30), Comparison(10)),
                new AdminSettlementComparisons(Comparison(50))),
            [],
            [],
            DateTime.SpecifyKind(new DateTime(2026, 8, 7), DateTimeKind.Utc));

        await cache.SetAsync("revenue:v2:test", expected, TimeSpan.FromSeconds(60));
        var actual = await cache.GetAsync<AdminRevenueAnalyticsResponse>("revenue:v2:test");

        store.Expiration.Should().Be(TimeSpan.FromSeconds(60));
        actual.Should().BeEquivalentTo(expected);
    }

    private static VietRide.Payment.Application.Features.RevenueAnalytics.Core.RevenueComparison Comparison(long value)
        => new(value, 0, null, "UP");

    private sealed class FakeRevenueCacheStore : IRevenueCacheStore
    {
        private string? value;
        public TimeSpan? Expiration { get; private set; }

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
            => Task.FromResult(value);

        public Task SetAsync(
            string key,
            string value,
            TimeSpan expiration,
            CancellationToken cancellationToken)
        {
            this.value = value;
            Expiration = expiration;
            return Task.CompletedTask;
        }
    }
}
