using FluentAssertions;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Features.Internal.Revenue.RevenueSummary;
using VietRide.Payment.Application.Features.RevenueAnalytics.Core;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.UnitTests.Features.RevenueAnalytics;

public sealed class RevenueAnalyticsInternalSummaryHandlerTests
{
    [Fact]
    public async Task HandlersUseCanonicalTotalsAndVersionedSixtySecondCache()
    {
        var operatorId = Guid.NewGuid();
        var repository = new StubRepository
        {
            AdminRows =
            [
                new AdminRevenueMonthReadModel(new DateOnly(2026, 7, 1), 500, -50, 200, 300),
            ],
            OperatorSummary = new OperatorRevenueSummaryReadModel(500, -50, 100, -150),
        };
        var cache = new MemoryRevenueCache();
        var clock = new TestClock();
        var adminHandler = new GetInternalAdminRevenueSummaryQueryHandler(repository, cache, clock);
        var operatorHandler = new GetInternalOperatorRevenueSummaryQueryHandler(repository, cache, clock);

        var admin = await adminHandler.Handle(
            new GetInternalAdminRevenueSummaryQuery("2026-07-01", "2026-07-31"),
            CancellationToken.None);
        var operatorSummary = await operatorHandler.Handle(
            new GetInternalOperatorRevenueSummaryQuery(operatorId, "2026-07-01", "2026-07-31"),
            CancellationToken.None);
        var cachedAdmin = await adminHandler.Handle(
            new GetInternalAdminRevenueSummaryQuery("2026-07-01", "2026-07-31"),
            CancellationToken.None);

        admin.TotalProjectRevenueVnd.Should().Be(650);
        admin.NetTransportRevenueVnd.Should().Be(450);
        admin.SubscriptionRevenueVnd.Should().Be(200);
        admin.PaidToOperatorsVnd.Should().Be(300);
        operatorSummary.NetRevenueVnd.Should().Be(450);
        operatorSummary.GrossParcelRevenueVnd.Should().Be(100);
        operatorSummary.ParcelRefundsVnd.Should().Be(-150);
        cachedAdmin.Should().Be(admin);
        repository.AdminCallCount.Should().Be(1);
        repository.OperatorCallCount.Should().Be(1);
        cache.Expirations.Should().OnlyContain(expiration => expiration == TimeSpan.FromSeconds(60));
        cache.Keys.Should().OnlyContain(key => key.StartsWith("revenue:v2:", StringComparison.Ordinal));
    }

    private sealed class StubRepository : IRevenueAnalyticsRepository
    {
        public IReadOnlyList<AdminRevenueMonthReadModel> AdminRows { get; set; } = [];
        public OperatorRevenueSummaryReadModel OperatorSummary { get; set; } = new(0, 0, 0, 0);
        public int AdminCallCount { get; private set; }
        public int OperatorCallCount { get; private set; }

        public Task<IReadOnlyList<AdminRevenueMonthReadModel>> GetAdminMonthlyRevenueAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken = default)
        {
            AdminCallCount++;
            return Task.FromResult(AdminRows);
        }

        public Task<OperatorRevenueSummaryReadModel> GetOperatorRevenueSummaryAsync(
            Guid operatorId,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken = default)
        {
            OperatorCallCount++;
            return Task.FromResult(OperatorSummary);
        }

        public Task<IReadOnlyList<TopOperatorRevenueReadModel>> GetTopOperatorRevenueAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            int top,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<OperatorRevenueLedgerReadModel>> GetOperatorRevenueLedgerAsync(
            Guid operatorId,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class MemoryRevenueCache : IRevenueReportCache
    {
        private readonly Dictionary<string, object> values = new(StringComparer.Ordinal);
        public List<string> Keys { get; } = [];
        public List<TimeSpan> Expirations { get; } = [];

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
            where T : class
            => Task.FromResult(values.GetValueOrDefault(key) as T);

        public Task SetAsync<T>(
            string key,
            T value,
            TimeSpan expiration,
            CancellationToken cancellationToken = default)
            where T : class
        {
            values[key] = value;
            Keys.Add(key);
            Expirations.Add(expiration);
            return Task.CompletedTask;
        }
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-08-07T00:00:00Z");
    }
}
