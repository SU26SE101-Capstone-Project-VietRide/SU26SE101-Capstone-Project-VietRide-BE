using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Payment.Application.Abstractions.Refunds;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Infrastructure.Jobs;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.UnitTests.Infrastructure.Jobs;

public sealed class RefundFailureRetryJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_WhenRetrySucceeds_ResolvesFailureLog()
    {
        var failure = CreateBookingFailure();
        var repository = new FakeRefundFailureLogRepository(failure);
        var executor = new FakeRefundRetryExecutor(RefundRetryExecutionResult.Success());
        var unitOfWork = new FakeUnitOfWork();
        var job = CreateJob(repository, executor, unitOfWork);

        await job.RunAsync(CancellationToken.None);

        failure.IsResolved.Should().BeTrue();
        failure.ResolvedAt.Should().Be(Now);
        failure.RetryCount.Should().Be(0);
        executor.ExecutedFailures.Should().ContainSingle().Which.Should().BeSameAs(failure);
        unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_WhenRetryFails_RecordsAttemptAndLeavesUnresolved()
    {
        var failure = CreateBookingFailure();
        var repository = new FakeRefundFailureLogRepository(failure);
        var executor = new FakeRefundRetryExecutor(RefundRetryExecutionResult.Failure("Wallet credit still failed."));
        var unitOfWork = new FakeUnitOfWork();
        var job = CreateJob(repository, executor, unitOfWork);

        await job.RunAsync(CancellationToken.None);

        failure.IsResolved.Should().BeFalse();
        failure.RetryCount.Should().Be(1);
        failure.LastAttemptAt.Should().Be(Now);
        failure.FailureReason.Should().Be("Wallet credit still failed.");
        unitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_WhenFailureReachesMaxRetry_RecordsRetryExhaustedAndStopsFutureRetry()
    {
        var failure = CreateBookingFailure();
        for (var i = 0; i < 4; i++)
        {
            failure.RecordRetryFailure(Now.AddMinutes(-5 + i), "Previous retry failed.");
        }

        var repository = new FakeRefundFailureLogRepository(failure);
        var executor = new FakeRefundRetryExecutor(RefundRetryExecutionResult.Failure("Wallet credit still failed."));
        var unitOfWork = new FakeUnitOfWork();
        var job = CreateJob(repository, executor, unitOfWork);

        await job.RunAsync(CancellationToken.None);

        failure.IsResolved.Should().BeFalse();
        failure.RetryCount.Should().Be(5);
        failure.IsRetryExhausted.Should().BeTrue();
        failure.FailureReason.Should().StartWith("REFUND_RETRY_EXHAUSTED:");
        executor.ExecutedFailures.Should().ContainSingle();

        await job.RunAsync(CancellationToken.None);

        executor.ExecutedFailures.Should().ContainSingle();
        failure.RetryCount.Should().Be(5);
    }

    [Fact]
    public void RefundFailureLog_WhenRetryAttemptIsRecorded_IncrementsRetryCountAndLastAttempt()
    {
        var failure = CreateBookingFailure();

        failure.RecordRetryAttempt(Now);

        failure.RetryCount.Should().Be(1);
        failure.LastAttemptAt.Should().Be(Now);
        failure.CanRetry.Should().BeTrue();
        RefundFailureRetryJob.RecurringJobId.Should().Be("payment.refund-failure-retry");
    }

    private static RefundFailureRetryJob CreateJob(
        IRefundFailureLogRepository repository,
        IRefundRetryExecutor executor,
        IUnitOfWork unitOfWork)
        => new(
            repository,
            executor,
            unitOfWork,
            new FrozenClock(Now),
            NullLogger<RefundFailureRetryJob>.Instance);

    private static RefundFailureLog CreateBookingFailure()
        => RefundFailureLog.CreateForBooking(
            Guid.NewGuid(),
            "booking.booking.cancelled",
            "Wallet credit failed.",
            Now.AddMinutes(-10));

    private sealed class FakeRefundFailureLogRepository : IRefundFailureLogRepository
    {
        private readonly List<RefundFailureLog> _failures;

        public FakeRefundFailureLogRepository(params RefundFailureLog[] failures)
        {
            _failures = [.. failures];
        }

        public Task<RefundFailureLog?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_failures.FirstOrDefault(failure => failure.Id == id));

        public Task<RefundFailureLog> AddAsync(RefundFailureLog entity, CancellationToken ct)
        {
            _failures.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(RefundFailureLog entity)
        {
        }

        public void Remove(RefundFailureLog entity)
            => _failures.Remove(entity);

        public IQueryable<RefundFailureLog> Query()
            => _failures.AsQueryable();

        public IQueryable<RefundFailureLog> QueryNoTracking()
            => _failures.AsQueryable();

        public Task<IReadOnlyList<RefundFailureLog>> GetUnresolvedAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RefundFailureLog>>(
                _failures
                    .Where(failure => failure.ResolvedAt == null)
                    .OrderBy(failure => failure.LastAttemptAt)
                    .ToList());

        public Task<IReadOnlyList<RefundFailureLog>> GetRetryableAsync(
            int maxRetryCount,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RefundFailureLog>>(
                _failures
                    .Where(failure => failure.ResolvedAt == null && failure.RetryCount < maxRetryCount)
                    .OrderBy(failure => failure.LastAttemptAt)
                    .ToList());
    }

    private sealed class FakeRefundRetryExecutor : IRefundRetryExecutor
    {
        private readonly Queue<RefundRetryExecutionResult> _results;

        public FakeRefundRetryExecutor(params RefundRetryExecutionResult[] results)
        {
            _results = new Queue<RefundRetryExecutionResult>(results);
        }

        public List<RefundFailureLog> ExecutedFailures { get; } = [];

        public Task<RefundRetryExecutionResult> ExecuteAsync(
            RefundFailureLog failure,
            CancellationToken cancellationToken)
        {
            ExecutedFailures.Add(failure);
            return Task.FromResult(_results.Count > 0
                ? _results.Dequeue()
                : RefundRetryExecutionResult.Failure("No fake retry result was configured."));
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct)
        {
            SaveChangesCount++;
            return Task.FromResult(1);
        }

        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct)
            => operation();

        public Task BeginTransactionAsync(CancellationToken ct)
            => Task.CompletedTask;

        public Task CommitAsync(CancellationToken ct)
            => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class FrozenClock : IClock
    {
        public FrozenClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
