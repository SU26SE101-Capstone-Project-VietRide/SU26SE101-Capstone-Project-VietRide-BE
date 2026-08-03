using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.FareSurcharges;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Features.FareSurcharges;

public sealed class FareSurchargeCommandHandlerTests
{
    private static readonly DateOnly StartDate = new(2026, 2, 10);

    [Fact]
    public async Task Create_InactivePeriod_AllowsOverlap()
    {
        var operatorId = Guid.NewGuid();
        var existing = OperatorFareSurchargePeriod.Create(
            operatorId, "Existing", StartDate, StartDate.AddDays(10), 20, true);
        var repository = new FakePeriodRepository([existing]);
        var handler = new CreateFareSurchargePeriodCommandHandler(
            new AllowedIdentityClient(), repository, new FakeUnitOfWork(), new FrozenClock());

        var result = await handler.Handle(
            new CreateFareSurchargePeriodCommand(
                operatorId, "Disabled draft", StartDate.AddDays(5), StartDate.AddDays(6), 30, false),
            CancellationToken.None);

        result.IsActive.Should().BeFalse();
        repository.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Update_ActivatingOverlappingPeriod_ReturnsRegisteredErrorCode()
    {
        var operatorId = Guid.NewGuid();
        var active = OperatorFareSurchargePeriod.Create(
            operatorId, "Active", StartDate, StartDate.AddDays(10), 20, true);
        var draft = OperatorFareSurchargePeriod.Create(
            operatorId, "Draft", StartDate.AddDays(5), StartDate.AddDays(6), 30, false);
        var repository = new FakePeriodRepository([active, draft]);
        var handler = new UpdateFareSurchargePeriodCommandHandler(
            new AllowedIdentityClient(), repository, new FakeUnitOfWork(), new FrozenClock());

        var action = () => handler.Handle(
            new UpdateFareSurchargePeriodCommand(operatorId, draft.Id, null, null, null, null, true),
            CancellationToken.None);

        (await action.Should().ThrowAsync<CodedValidationException>())
            .Which.ErrorCode.Should().Be("FARE_SURCHARGE_PERIOD_OVERLAP");
        draft.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Update_DifferentOperatorCannotSeePeriod()
    {
        var ownerId = Guid.NewGuid();
        var period = OperatorFareSurchargePeriod.Create(
            ownerId, "Owner period", StartDate, StartDate.AddDays(1), 10, true);
        var handler = new UpdateFareSurchargePeriodCommandHandler(
            new AllowedIdentityClient(),
            new FakePeriodRepository([period]),
            new FakeUnitOfWork(),
            new FrozenClock());

        var action = () => handler.Handle(
            new UpdateFareSurchargePeriodCommand(Guid.NewGuid(), period.Id, "Hidden", null, null, null, null),
            CancellationToken.None);

        (await action.Should().ThrowAsync<CodedNotFoundException>())
            .Which.ErrorCode.Should().Be("FARE_SURCHARGE_PERIOD_NOT_FOUND");
    }

    [Fact]
    public async Task Delete_SoftDeletesAndDeactivatesPeriod()
    {
        var operatorId = Guid.NewGuid();
        var period = OperatorFareSurchargePeriod.Create(
            operatorId, "Holiday", StartDate, StartDate.AddDays(1), 10, true);
        var repository = new FakePeriodRepository([period]);
        var clock = new FrozenClock();
        var handler = new DeleteFareSurchargePeriodCommandHandler(
            new AllowedIdentityClient(), repository, new FakeUnitOfWork(), clock);

        await handler.Handle(
            new DeleteFareSurchargePeriodCommand(operatorId, period.Id),
            CancellationToken.None);

        period.DeletedAt.Should().Be(clock.UtcNow);
        period.IsActive.Should().BeFalse();
        repository.QueryNoTracking().Should().BeEmpty();
    }

    private sealed class FakePeriodRepository(List<OperatorFareSurchargePeriod> items)
        : IOperatorFareSurchargePeriodRepository
    {
        public List<OperatorFareSurchargePeriod> Items { get; } = items;

        public Task<OperatorFareSurchargePeriod?> GetByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(Items.FirstOrDefault(period => period.Id == id && period.DeletedAt is null));

        public Task<OperatorFareSurchargePeriod?> GetOwnedByIdAsync(
            Guid operatorId,
            Guid periodId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(period =>
                period.Id == periodId && period.OperatorId == operatorId && period.DeletedAt is null));

        public Task<bool> ExistsActiveOverlapAsync(
            Guid operatorId,
            DateOnly startDate,
            DateOnly endDate,
            Guid? excludedPeriodId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Any(period =>
                period.OperatorId == operatorId
                && period.DeletedAt is null
                && period.IsActive
                && (!excludedPeriodId.HasValue || period.Id != excludedPeriodId.Value)
                && period.StartDate <= endDate
                && period.EndDate >= startDate));

        public Task<OperatorFareSurchargePeriod?> GetActiveForDateAsync(
            Guid operatorId,
            DateOnly departureDate,
            CancellationToken cancellationToken = default) => Task.FromResult<OperatorFareSurchargePeriod?>(null);

        public Task<OperatorFareSurchargePeriod> AddAsync(
            OperatorFareSurchargePeriod entity,
            CancellationToken ct)
        {
            Items.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(OperatorFareSurchargePeriod entity) { }
        public void Remove(OperatorFareSurchargePeriod entity) => Items.Remove(entity);
        public IQueryable<OperatorFareSurchargePeriod> Query() =>
            Items.Where(period => period.DeletedAt is null).AsQueryable();
        public IQueryable<OperatorFareSurchargePeriod> QueryNoTracking() => Query();
    }

    private sealed class AllowedIdentityClient : IIdentityInternalClient
    {
        public Task<OperatorWriteEligibilityValidation> ValidateOperatorCanWriteAsync(
            Guid operatorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperatorWriteEligibilityValidation.Allowed());

        public Task<IdentityUserLookupResult> GetUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(IdentityUserLookupResult.ValidationFailure("Not used."));
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(1);
        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct) => operation();
        public Task BeginTransactionAsync(CancellationToken ct) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FrozenClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
