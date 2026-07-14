using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.TopUps.ExpireTopUp;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Infrastructure.Jobs;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.UnitTests.Features.TopUps.ExpireTopUp;

public sealed class ExpireTopUpCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_WhenPendingTopUpIsOlderThan15Minutes_ExpiresIt()
    {
        var stalePending = CreateTopUp(createdAt: Now.AddMinutes(-16));
        var repository = new FakeTopUpRequestRepository(stalePending);
        var handler = CreateHandler(repository);

        var result = await handler.Handle(new ExpireTopUpCommand(Now), CancellationToken.None);

        result.ExpiredCount.Should().Be(1);
        stalePending.Status.Should().Be(TopUpRequestStatus.EXPIRED);
        stalePending.ExpiredAt.Should().Be(Now);
        repository.LastExpiresBefore.Should().Be(Now.AddMinutes(-10));
    }

    [Fact]
    public async Task Handle_WhenPendingTopUpIsExactly15MinutesOld_LeavesItPending()
    {
        var exactlyAtBoundary = CreateTopUp(createdAt: Now.AddMinutes(-10));
        var repository = new FakeTopUpRequestRepository(exactlyAtBoundary);
        var handler = CreateHandler(repository);

        var result = await handler.Handle(new ExpireTopUpCommand(Now), CancellationToken.None);

        result.ExpiredCount.Should().Be(0);
        exactlyAtBoundary.Status.Should().Be(TopUpRequestStatus.PENDING);
        exactlyAtBoundary.ExpiredAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenTopUpsAreTerminal_LeavesThemUntouched()
    {
        var succeeded = CreateTopUp(createdAt: Now.AddMinutes(-30));
        succeeded.MarkSucceeded("00", Now.AddMinutes(-20));
        var failed = CreateTopUp(createdAt: Now.AddMinutes(-30));
        failed.MarkFailed("24");
        var expired = CreateTopUp(createdAt: Now.AddMinutes(-30));
        expired.MarkExpired(Now.AddMinutes(-20));
        var repository = new FakeTopUpRequestRepository(succeeded, failed, expired);
        var handler = CreateHandler(repository);

        var result = await handler.Handle(new ExpireTopUpCommand(Now), CancellationToken.None);

        result.ExpiredCount.Should().Be(0);
        succeeded.Status.Should().Be(TopUpRequestStatus.SUCCEEDED);
        failed.Status.Should().Be(TopUpRequestStatus.FAILED);
        expired.Status.Should().Be(TopUpRequestStatus.EXPIRED);
        expired.ExpiredAt.Should().Be(Now.AddMinutes(-20));
    }

    [Fact]
    public async Task Handle_WhenRerunAfterExpiry_IsIdempotent()
    {
        var stalePending = CreateTopUp(createdAt: Now.AddMinutes(-16));
        var repository = new FakeTopUpRequestRepository(stalePending);
        var handler = CreateHandler(repository);

        var firstRun = await handler.Handle(new ExpireTopUpCommand(Now), CancellationToken.None);
        var secondRun = await handler.Handle(new ExpireTopUpCommand(Now.AddMinutes(1)), CancellationToken.None);

        firstRun.ExpiredCount.Should().Be(1);
        secondRun.ExpiredCount.Should().Be(0);
        stalePending.Status.Should().Be(TopUpRequestStatus.EXPIRED);
        stalePending.ExpiredAt.Should().Be(Now);
    }

    [Fact]
    public async Task RunAsync_DelegatesToMediatRCommand()
    {
        var mediator = new RecordingMediator(new ExpireTopUpResult(3));
        var job = new TopUpExpiredJob(mediator, NullLogger<TopUpExpiredJob>.Instance);

        await job.RunAsync(CancellationToken.None);

        mediator.Requests.Should().ContainSingle(request => request is ExpireTopUpCommand);
    }

    private static ExpireTopUpCommandHandler CreateHandler(FakeTopUpRequestRepository repository)
        => new(
            repository,
            new FrozenClock(Now),
            NullLogger<ExpireTopUpCommandHandler>.Instance);

    private static TopUpRequest CreateTopUp(DateTimeOffset createdAt)
    {
        var topUp = TopUpRequest.Create(
            Guid.NewGuid(),
            Money.FromRaw(100_000),
            Guid.NewGuid().ToString());
        topUp.CreatedAt = createdAt;
        topUp.UpdatedAt = createdAt;
        return topUp;
    }

    private sealed class FakeTopUpRequestRepository : ITopUpRequestRepository
    {
        private readonly List<TopUpRequest> _topUps;

        public FakeTopUpRequestRepository(params TopUpRequest[] topUps)
        {
            _topUps = topUps.ToList();
        }

        public DateTimeOffset? LastExpiresBefore { get; private set; }

        public Task<TopUpRequest?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_topUps.FirstOrDefault(x => x.Id == id));

        public Task<TopUpRequest> AddAsync(TopUpRequest entity, CancellationToken ct)
        {
            _topUps.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(TopUpRequest entity)
        {
        }

        public void Remove(TopUpRequest entity)
            => _topUps.Remove(entity);

        public IQueryable<TopUpRequest> Query()
            => _topUps.AsQueryable();

        public IQueryable<TopUpRequest> QueryNoTracking()
            => _topUps.AsQueryable();

        public Task<TopUpRequest?> FindByVnPayTxnRefAsync(string vnPayTxnRef, CancellationToken cancellationToken)
            => Task.FromResult(_topUps.FirstOrDefault(x => x.VnPayTxnRef == vnPayTxnRef));

        public Task<int> ExpirePendingOlderThanAsync(
            DateTimeOffset expiresBefore,
            DateTimeOffset expiredAt,
            CancellationToken cancellationToken)
        {
            LastExpiresBefore = expiresBefore;
            var expired = _topUps
                .Where(topUp => topUp.Status == TopUpRequestStatus.PENDING && topUp.CreatedAt < expiresBefore)
                .ToList();

            foreach (var topUp in expired)
            {
                topUp.MarkExpired(expiredAt);
            }

            return Task.FromResult(expired.Count);
        }
    }

    private sealed class FrozenClock : IClock
    {
        public FrozenClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class RecordingMediator : IMediator
    {
        private readonly ExpireTopUpResult _result;

        public RecordingMediator(ExpireTopUpResult result)
        {
            _result = result;
        }

        public List<object> Requests { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult((TResponse)(object)_result);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult<object?>(_result);
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Top-up expiration tests do not use streaming MediatR requests.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Top-up expiration tests do not use streaming MediatR requests.");

        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;
    }
}
