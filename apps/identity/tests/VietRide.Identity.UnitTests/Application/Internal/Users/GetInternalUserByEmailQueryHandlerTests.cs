using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.InternalUsers.GetInternalUserByEmail;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.UnitTests.Application.Internal.Users;

public sealed class GetInternalUserByEmailQueryHandlerTests
{
    [Fact]
    public async Task Handle_NormalizesEmailAndReturnsOnlyUserId()
    {
        var users = Substitute.For<IUserRepository>();
        var user = User.CreateOperatorScopedPendingPassword(
            "recipient@example.com", PhoneNumber.Parse("+84901234567"), "Recipient", UserRole.DRIVER, Guid.NewGuid());
        users.GetByEmailAsync("recipient@example.com", Arg.Any<CancellationToken>()).Returns(user);

        var result = await new GetInternalUserByEmailQueryHandler(users)
            .Handle(new GetInternalUserByEmailQuery(" Recipient@Example.COM "), CancellationToken.None);

        result.Should().Be(new GetInternalUserByEmailResponseDto(user.Id));
    }

    [Fact]
    public async Task Handle_NoMatch_ThrowsResourceNotFoundException()
    {
        var users = Substitute.For<IUserRepository>();
        var act = () => new GetInternalUserByEmailQueryHandler(users)
            .Handle(new GetInternalUserByEmailQuery("missing@example.com"), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
