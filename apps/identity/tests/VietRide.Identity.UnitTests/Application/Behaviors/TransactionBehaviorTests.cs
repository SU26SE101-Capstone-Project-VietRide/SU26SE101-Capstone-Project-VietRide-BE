using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Shared.Application.Behaviors;
using VietRide.Shared.Application.Cqrs;
using VietRide.Shared.Application.UnitOfWork;

namespace VietRide.Identity.UnitTests.Application.Behaviors;

public sealed class TransactionBehaviorTests
{
    private sealed record SampleCommand : IRequest<string>;

    private sealed record SampleQuery : IQuery<string>;

    private sealed record MisnamedReadRequest : IQuery<string>;

    [Fact]
    public async Task Handle_Command_ExecutesHandlerInsideUnitOfWorkTransaction()
    {
        var unitOfWork = new SpyUnitOfWork();
        var behavior = BuildBehavior<SampleCommand>(unitOfWork);
        var handlerWasCalled = false;

        var result = await behavior.Handle(
            new SampleCommand(),
            () =>
            {
                handlerWasCalled = true;
                return Task.FromResult("ok");
            },
            CancellationToken.None);

        result.Should().Be("ok");
        handlerWasCalled.Should().BeTrue();
        unitOfWork.ExecuteInTransactionCallCount.Should().Be(1);
        unitOfWork.BeginTransactionCallCount.Should().Be(0,
            "TransactionBehavior delegates the retry-safe transaction boundary to IUnitOfWork.ExecuteInTransactionAsync.");
        unitOfWork.CommitCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_Query_BypassesUnitOfWorkTransaction()
    {
        var unitOfWork = new SpyUnitOfWork();
        var behavior = BuildBehavior<SampleQuery>(unitOfWork);

        var result = await behavior.Handle(
            new SampleQuery(),
            () => Task.FromResult("jwks"),
            CancellationToken.None);

        result.Should().Be("jwks");
        unitOfWork.ExecuteInTransactionCallCount.Should().Be(0,
            "read-only Query requests must not open and commit a database transaction.");
    }

    [Fact]
    public async Task Handle_IQueryMarker_BypassesUnitOfWorkTransaction_EvenWhenNameDoesNotEndWithQuery()
    {
        var unitOfWork = new SpyUnitOfWork();
        var behavior = BuildBehavior<MisnamedReadRequest>(unitOfWork);

        var result = await behavior.Handle(
            new MisnamedReadRequest(),
            () => Task.FromResult("read"),
            CancellationToken.None);

        result.Should().Be("read");
        unitOfWork.ExecuteInTransactionCallCount.Should().Be(0,
            "TransactionBehavior should use the IQuery marker instead of request name suffixes.");
    }

    [Fact]
    public async Task Handle_Command_WhenHandlerThrows_PropagatesException()
    {
        var unitOfWork = new SpyUnitOfWork();
        var behavior = BuildBehavior<SampleCommand>(unitOfWork);

        var act = async () => await behavior.Handle(
            new SampleCommand(),
            () => throw new InvalidOperationException("handler failed"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("handler failed");
        unitOfWork.ExecuteInTransactionCallCount.Should().Be(1);
    }

    private static TransactionBehavior<TRequest, string> BuildBehavior<TRequest>(IUnitOfWork unitOfWork)
        where TRequest : IRequest<string>
        => new(NullLogger<TransactionBehavior<TRequest, string>>.Instance, unitOfWork);

    private sealed class SpyUnitOfWork : IUnitOfWork
    {
        public int ExecuteInTransactionCallCount { get; private set; }
        public int BeginTransactionCallCount { get; private set; }
        public int CommitCallCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(0);

        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct)
        {
            ExecuteInTransactionCallCount++;
            return operation();
        }

        public Task BeginTransactionAsync(CancellationToken ct)
        {
            BeginTransactionCallCount++;
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken ct)
        {
            CommitCallCount++;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
