using FluentAssertions;
using VietRide.Shared.Application.Repositories;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Services;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Features.FareSurcharges;

public sealed class FareSurchargeServiceTests
{
    [Fact]
    public async Task ResolveAsync_DisabledSetting_DoesNotResolvePeriod()
    {
        var operatorId = Guid.NewGuid();
        var periods = new FakePeriodRepository([]);
        var service = new FareSurchargeService(
            new FakeSettingRepository([OperatorFareSurchargeSetting.Create(operatorId, false)]),
            periods);

        var result = await service.ResolveAsync(
            operatorId,
            new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero));

        result.Should().BeNull();
        periods.ActiveDateLookups.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_UsesInclusiveIctDepartureDate()
    {
        var operatorId = Guid.NewGuid();
        var period = OperatorFareSurchargePeriod.Create(
            operatorId,
            "Tet",
            new DateOnly(2026, 2, 10),
            new DateOnly(2026, 2, 20),
            30,
            true);
        var periods = new FakePeriodRepository([period]);
        var service = new FareSurchargeService(
            new FakeSettingRepository([OperatorFareSurchargeSetting.Create(operatorId, true)]),
            periods);

        var startBoundary = await service.ResolveAsync(
            operatorId,
            new DateTimeOffset(2026, 2, 9, 17, 0, 0, TimeSpan.Zero));
        var endBoundary = await service.ResolveAsync(
            operatorId,
            new DateTimeOffset(2026, 2, 20, 16, 59, 59, TimeSpan.Zero));

        startBoundary.Should().Be(new FareSurchargeRule(period.Id, "Tet", 30));
        endBoundary.Should().Be(new FareSurchargeRule(period.Id, "Tet", 30));
        periods.ActiveDateLookups.Should().Equal(
            new DateOnly(2026, 2, 10),
            new DateOnly(2026, 2, 20));
    }

    [Theory]
    [InlineData(105, 15, 121, 16)]
    [InlineData(1, 50, 2, 1)]
    [InlineData(250000, 100, 500000, 250000)]
    public void Apply_RoundsVndAwayFromZero(
        long originalFare,
        int percent,
        long expectedEffectiveFare,
        long expectedSurchargeAmount)
    {
        var service = new FareSurchargeService(new FakeSettingRepository([]), new FakePeriodRepository([]));
        var rule = new FareSurchargeRule(Guid.NewGuid(), "Holiday", percent);

        var result = service.Apply(originalFare, rule);

        result.OriginalFare.Should().Be(originalFare);
        result.EffectiveFare.Should().Be(expectedEffectiveFare);
        result.SurchargeAmount.Should().Be(expectedSurchargeAmount);
        result.SurchargePercent.Should().Be(percent);
    }

    private abstract class FakeRepository<TEntity, TId> : IRepository<TEntity, TId>
        where TEntity : class
        where TId : notnull
    {
        private readonly Func<TEntity, TId> idSelector;

        protected FakeRepository(List<TEntity> items, Func<TEntity, TId> idSelector)
        {
            Items = items;
            this.idSelector = idSelector;
        }

        protected List<TEntity> Items { get; }

        public Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(item =>
                EqualityComparer<TId>.Default.Equals(idSelector(item), id)));

        public Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            Items.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(TEntity entity) { }
        public void Remove(TEntity entity) => Items.Remove(entity);
        public IQueryable<TEntity> Query() => Items.AsQueryable();
        public IQueryable<TEntity> QueryNoTracking() => Items.AsQueryable();
    }

    private sealed class FakeSettingRepository(List<OperatorFareSurchargeSetting> items)
        : FakeRepository<OperatorFareSurchargeSetting, Guid>(items, setting => setting.Id),
            IOperatorFareSurchargeSettingRepository
    {
        public Task<OperatorFareSurchargeSetting?> GetByOperatorIdAsync(
            Guid operatorId,
            CancellationToken cancellationToken = default) => GetByIdAsync(operatorId, cancellationToken);
    }

    private sealed class FakePeriodRepository(List<OperatorFareSurchargePeriod> items)
        : FakeRepository<OperatorFareSurchargePeriod, Guid>(items, period => period.Id),
            IOperatorFareSurchargePeriodRepository
    {
        public List<DateOnly> ActiveDateLookups { get; } = [];

        public Task<OperatorFareSurchargePeriod?> GetOwnedByIdAsync(
            Guid operatorId,
            Guid periodId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(period =>
                period.OperatorId == operatorId && period.Id == periodId && period.DeletedAt is null));

        public Task<bool> ExistsActiveOverlapAsync(
            Guid operatorId,
            DateOnly startDate,
            DateOnly endDate,
            Guid? excludedPeriodId,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<OperatorFareSurchargePeriod?> GetActiveForDateAsync(
            Guid operatorId,
            DateOnly departureDate,
            CancellationToken cancellationToken = default)
        {
            ActiveDateLookups.Add(departureDate);
            return Task.FromResult(Items.FirstOrDefault(period =>
                period.OperatorId == operatorId
                && period.IsActive
                && period.DeletedAt is null
                && period.StartDate <= departureDate
                && departureDate <= period.EndDate));
        }
    }
}
