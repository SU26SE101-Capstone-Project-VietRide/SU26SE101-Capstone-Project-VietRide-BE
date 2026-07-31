using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Infrastructure.Messaging;

namespace VietRide.Booking.UnitTests.Infrastructure;

public sealed class IdentityUserDeletedIntegrationEventHandlerTests
{
    [Fact]
    public async Task Handle_RedactsAllBuyerSnapshotsForDeletedUser()
    {
        var userId = Guid.NewGuid();
        var repository = Substitute.For<IBookingRepository>();
        var handler = new IdentityUserDeletedIntegrationEventHandler(repository);

        await handler.HandleAsync(
            new IdentityUserDeletedIntegrationEvent { UserId = userId },
            CancellationToken.None);

        await repository.Received(1).RedactBuyerSnapshotsAsync(
            userId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmptyUserIdRejectsWithoutWriting()
    {
        var repository = Substitute.For<IBookingRepository>();
        var handler = new IdentityUserDeletedIntegrationEventHandler(repository);

        var action = () => handler.HandleAsync(
            new IdentityUserDeletedIntegrationEvent(),
            CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>();
        await repository.DidNotReceive().RedactBuyerSnapshotsAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }
}
