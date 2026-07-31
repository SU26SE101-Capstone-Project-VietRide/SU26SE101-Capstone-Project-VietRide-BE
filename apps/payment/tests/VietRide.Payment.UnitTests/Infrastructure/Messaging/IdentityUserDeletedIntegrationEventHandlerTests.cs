using FluentAssertions;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Infrastructure.Messaging;

namespace VietRide.Payment.UnitTests.Infrastructure.Messaging;

public sealed class IdentityUserDeletedIntegrationEventHandlerTests
{
    [Fact]
    public async Task Handle_MarksDeletedAndRedactsFinancialActorSnapshots()
    {
        var userId = Guid.NewGuid();
        var privacy = new CapturingPrivacyStore();
        var handler = new IdentityUserDeletedIntegrationEventHandler(privacy);

        await handler.HandleAsync(
            new IdentityUserDeletedIntegrationEvent { UserId = userId },
            CancellationToken.None);

        privacy.RedactedUserIds.Should().Equal(userId);
    }

    [Fact]
    public async Task Handle_EmptyUserIdRejectsWithoutWriting()
    {
        var privacy = new CapturingPrivacyStore();
        var handler = new IdentityUserDeletedIntegrationEventHandler(privacy);

        var action = () => handler.HandleAsync(
            new IdentityUserDeletedIntegrationEvent(),
            CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>();
        privacy.RedactedUserIds.Should().BeEmpty();
    }

    private sealed class CapturingPrivacyStore : IFinancialActorPrivacyStore
    {
        public List<Guid> RedactedUserIds { get; } = [];

        public Task<bool> IsDeletedWithLockAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<int> MarkDeletedAndRedactAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            RedactedUserIds.Add(userId);
            return Task.FromResult(1);
        }
    }
}
