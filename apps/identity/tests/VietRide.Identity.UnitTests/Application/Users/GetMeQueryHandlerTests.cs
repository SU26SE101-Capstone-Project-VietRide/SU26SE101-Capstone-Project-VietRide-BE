using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Users.GetMe;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.UnitTests.Application.Users;

public sealed class GetMeQueryHandlerTests
{
    [Fact]
    public async Task Handle_HappyPath_ReturnsCallerProfile()
    {
        var users = Substitute.For<IUserRepository>();
        var user = User.CreatePassenger(
            "user@example.com",
            PhoneNumber.Parse("+84901234567"),
            "$2a$12$hashedpassword",
            "Test User");
        user.VerifyEmail();
        var handler = new GetMeQueryHandler(users);

        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await handler.Handle(new GetMeQuery(user.Id), CancellationToken.None);

        result.Should().BeEquivalentTo(new GetMeResponseDto(
            Id: user.Id,
            Email: "user@example.com",
            DisplayName: "Test User",
            Phone: "+84901234567",
            Role: UserRole.PASSENGER.ToString(),
            OperatorId: null,
            Status: UserStatus.ACTIVE.ToString(),
            AvatarUrl: null));
    }

    [Fact]
    public async Task Handle_WhenCallerDoesNotExist_ThrowsNotFound()
    {
        var users = Substitute.For<IUserRepository>();
        var userId = Guid.NewGuid();
        var handler = new GetMeQueryHandler(users);

        users.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var act = () => handler.Handle(new GetMeQuery(userId), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
