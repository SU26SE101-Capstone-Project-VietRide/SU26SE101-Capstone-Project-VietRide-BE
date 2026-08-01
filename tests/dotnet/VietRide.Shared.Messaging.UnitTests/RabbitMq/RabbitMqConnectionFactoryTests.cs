using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using VietRide.Shared.Messaging.RabbitMq;
using Xunit;

namespace VietRide.Shared.Messaging.UnitTests.RabbitMq;

public sealed class RabbitMqConnectionFactoryTests
{
    [Fact]
    public void CreateClientFactory_UsesConfiguredConnectionAttemptTimeout()
    {
        using var factory = CreateFactory(
            () => Substitute.For<IConnection>(),
            connectionAttemptTimeoutSeconds: 7);

        factory.CreateClientFactory().RequestedConnectionTimeout.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveConnectionAttemptTimeout(int timeoutSeconds)
    {
        var act = () => CreateFactory(
            () => Substitute.For<IConnection>(),
            connectionAttemptTimeoutSeconds: timeoutSeconds);

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*ConnectionAttemptTimeoutSeconds must be greater than zero*");
    }

    [Fact]
    public async Task GetOrCreate_BlockedFailingCreator_DoesNotBlockConcurrentCreator()
    {
        var firstCreatorEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCreatorEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseFirstCreator = new ManualResetEventSlim();
        var openConnection = Substitute.For<IConnection>();
        openConnection.IsOpen.Returns(true);
        var createCount = 0;
        using var factory = CreateFactory(() =>
        {
            var call = Interlocked.Increment(ref createCount);
            if (call == 1)
            {
                firstCreatorEntered.TrySetResult();
                if (!releaseFirstCreator.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Timed out waiting to release the first connection creator.");
            }

            if (call == 2)
            {
                secondCreatorEntered.TrySetResult();
                return openConnection;
            }

            throw new BrokerUnreachableException(new InvalidOperationException("broker unavailable"));
        });

        var firstCall = Task.Run(factory.GetOrCreate);
        await firstCreatorEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var secondCall = Task.Run(factory.GetOrCreate);

        try
        {
            await secondCreatorEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            (await secondCall.WaitAsync(TimeSpan.FromSeconds(2))).Should().BeSameAs(openConnection);
        }
        finally
        {
            releaseFirstCreator.Set();
        }

        var firstCallAct = async () => await firstCall;
        await firstCallAct.Should().ThrowAsync<BrokerUnreachableException>();
        createCount.Should().Be(3);
    }

    [Fact]
    public async Task GetOrCreate_ConcurrentSuccessfulCandidates_InstallOneAndDisposeLoser()
    {
        var firstCandidate = Substitute.For<IConnection>();
        var secondCandidate = Substitute.For<IConnection>();
        firstCandidate.IsOpen.Returns(true);
        secondCandidate.IsOpen.Returns(true);
        var bothCreatorsEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCreators = new ManualResetEventSlim();
        var createCount = 0;
        var factory = CreateFactory(() =>
        {
            var call = Interlocked.Increment(ref createCount);
            if (call == 2)
                bothCreatorsEntered.TrySetResult();

            if (!releaseCreators.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Timed out waiting to release concurrent connection creators.");

            return call == 1 ? firstCandidate : secondCandidate;
        });

        var firstCall = Task.Run(factory.GetOrCreate);
        var secondCall = Task.Run(factory.GetOrCreate);
        await bothCreatorsEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        releaseCreators.Set();

        var results = await Task.WhenAll(firstCall, secondCall).WaitAsync(TimeSpan.FromSeconds(2));
        results[0].Should().BeSameAs(results[1]);
        var installed = results[0];
        var loser = ReferenceEquals(installed, firstCandidate) ? secondCandidate : firstCandidate;
        loser.Received(1).Dispose();
        installed.DidNotReceive().Dispose();
        createCount.Should().Be(2);

        factory.Dispose();
        installed.Received(1).Dispose();
    }

    [Fact]
    public async Task GetOrCreate_DisposesCandidate_WhenFactoryIsDisposedDuringCreation()
    {
        var creatorEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCreator = new ManualResetEventSlim();
        var candidate = Substitute.For<IConnection>();
        candidate.IsOpen.Returns(true);
        var factory = CreateFactory(() =>
        {
            creatorEntered.TrySetResult();
            if (!releaseCreator.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Timed out waiting to release the connection creator.");

            return candidate;
        });

        var createCall = Task.Run(factory.GetOrCreate);
        await creatorEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        factory.Dispose();
        releaseCreator.Set();

        var createCallAct = async () => await createCall;
        await createCallAct.Should().ThrowAsync<ObjectDisposedException>();
        candidate.Received(1).Dispose();
    }

    private static RabbitMqConnectionFactory CreateFactory(
        Func<IConnection> createConnection,
        int connectionAttemptTimeoutSeconds = 5)
        => new(
            Options.Create(new RabbitMqOptions
            {
                ConnectionRetryCount = 1,
                ConnectionRetryBaseDelaySeconds = 0,
                ConnectionAttemptTimeoutSeconds = connectionAttemptTimeoutSeconds,
            }),
            Substitute.For<ILogger<RabbitMqConnectionFactory>>(),
            createConnection);
}
