using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.InternalUsers.GetInternalUserByPhone;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.UnitTests.Application.Internal.Users;

public sealed class GetInternalUserByPhoneQueryHandlerTests
{
    [Fact]
    public async Task Handle_Match_ReturnsOnlyUserId()
    {
        var users = Substitute.For<IUserRepository>();
        var user = User.CreateOperatorScopedPendingPassword(
            "driver@example.com", PhoneNumber.Parse("+84901234567"), "Driver", UserRole.DRIVER, Guid.NewGuid());
        users.GetByPhoneAsync("+84901234567", Arg.Any<CancellationToken>()).Returns(user);

        var result = await new GetInternalUserByPhoneQueryHandler(users)
            .Handle(new GetInternalUserByPhoneQuery("+84901234567"), CancellationToken.None);

        result.Should().Be(new GetInternalUserByPhoneResponseDto(user.Id));
    }

    [Fact]
    public async Task Handle_NoMatch_ThrowsResourceNotFoundException()
    {
        var users = Substitute.For<IUserRepository>();
        var act = () => new GetInternalUserByPhoneQueryHandler(users)
            .Handle(new GetInternalUserByPhoneQuery("+84901234567"), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }
}
