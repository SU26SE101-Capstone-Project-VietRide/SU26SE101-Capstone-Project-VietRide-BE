using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Internal.Operators.GetShuttleDispatchRecipientUsers;

namespace VietRide.Identity.UnitTests.Application.Internal.Operators;

public sealed class GetShuttleDispatchRecipientUsersQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsActiveAdminAndStaffRecipientsFromRepository()
    {
        var operatorId = Guid.NewGuid();
        var recipients = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var repository = Substitute.For<IUserRepository>();
        repository.ListActiveShuttleDispatchRecipientIdsAsync(
                operatorId,
                Arg.Any<CancellationToken>())
            .Returns(recipients);
        var handler = new GetShuttleDispatchRecipientUsersQueryHandler(repository);

        var result = await handler.Handle(
            new GetShuttleDispatchRecipientUsersQuery(operatorId),
            CancellationToken.None);

        result.Should().Equal(recipients);
        await repository.Received(1).ListActiveShuttleDispatchRecipientIdsAsync(
            operatorId,
            Arg.Any<CancellationToken>());
    }
}
