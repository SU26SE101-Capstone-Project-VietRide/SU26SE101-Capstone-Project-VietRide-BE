using FluentAssertions;
using MediatR;
using VietRide.Trip.Application.Features.DriverSchedules;

namespace VietRide.Trip.UnitTests.Features.DriverSchedules;

public sealed class UpdateDriverScheduleCrewAliasTests
{
    [Fact]
    public async Task Alias_DelegatesToCanonicalAllPendingCommand()
    {
        var sender = new CapturingSender();
        var command = new UpdateDriverScheduleCrewCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "request-1", Guid.NewGuid(), null);

        await new UpdateDriverScheduleCrewHandler(sender).Handle(command, CancellationToken.None);

        var canonical = sender.Request.Should().BeOfType<UpdateDriverScheduleCommand>().Subject;
        canonical.ApplyTo.Should().Be(UpdateDriverScheduleCommand.AllPending);
        canonical.DriverUserIdSpecified.Should().BeTrue();
        canonical.DriverUserId.Should().Be(command.DriverUserId);
        canonical.AssistantUserIdSpecified.Should().BeTrue();
        canonical.AssistantUserId.Should().BeNull();
    }

    private sealed class CapturingSender : ISender
    {
        public object? Request { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult((TResponse)(object)new DriverScheduleDto(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), null,
                [1], new TimeOnly(8, 0), new DateOnly(2026, 1, 1), null, true,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult<object?>(null);
        }

        public async IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public async IAsyncEnumerable<object?> CreateStream(
            object request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
