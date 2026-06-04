using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Admin.CreateAdminUser;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.UnitTests.Application.Admin;

public sealed class CreateAdminUserCommandHandlerTests
{
    [Fact]
    public async Task Handle_HappyPath_CreatesPasswordlessSystemAdminPendingInitialPassword()
    {
        var users = Substitute.For<IUserRepository>();
        var handler = new CreateAdminUserCommandHandler(users);
        User? capturedUser = null;

        users.GetByEmailAsync("admin@example.com", Arg.Any<CancellationToken>()).Returns((User?)null);
        users.AddAsync(Arg.Do<User>(user => capturedUser = user), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<User>());

        var result = await handler.Handle(
            new CreateAdminUserCommand(
                Guid.NewGuid(),
                UserRole.SYSTEM_ADMIN.ToString(),
                "Admin@Example.com",
                "Admin User",
                UserRole.SYSTEM_ADMIN.ToString()),
            CancellationToken.None);

        result.Email.Should().Be("admin@example.com");
        result.Role.Should().Be(UserRole.SYSTEM_ADMIN.ToString());
        result.Status.Should().Be(UserStatus.PENDING_INITIAL_PASSWORD.ToString());
        capturedUser.Should().NotBeNull();
        capturedUser!.PasswordHash.Should().BeNull();
        capturedUser.Phone.Should().BeNull();
        capturedUser.OperatorId.Should().BeNull();
        capturedUser.Status.Should().Be(UserStatus.PENDING_INITIAL_PASSWORD);
    }

    [Fact]
    public async Task Handle_NonSystemAdminCaller_Throws403Forbidden()
    {
        var users = Substitute.For<IUserRepository>();
        var handler = new CreateAdminUserCommandHandler(users);

        var act = () => handler.Handle(
            new CreateAdminUserCommand(
                Guid.NewGuid(),
                UserRole.PASSENGER.ToString(),
                "admin@example.com",
                "Admin User",
                UserRole.SYSTEM_ADMIN.ToString()),
            CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<ForbiddenException>();
        assertion.Which.ErrorCode.Should().Be("FORBIDDEN");
        await users.DidNotReceiveWithAnyArgs().GetByEmailAsync(default!, default);
        await users.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Handle_DuplicateEmail_Throws409AuthEmailAlreadyRegistered()
    {
        var users = Substitute.For<IUserRepository>();
        var existing = User.CreateAdminPendingPassword("admin@example.com", "Existing Admin");
        var handler = new CreateAdminUserCommandHandler(users);

        users.GetByEmailAsync("admin@example.com", Arg.Any<CancellationToken>()).Returns(existing);

        var act = () => handler.Handle(
            new CreateAdminUserCommand(
                Guid.NewGuid(),
                UserRole.SYSTEM_ADMIN.ToString(),
                "admin@example.com",
                "Admin User",
                UserRole.SYSTEM_ADMIN.ToString()),
            CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<ConflictException>();
        assertion.Which.ErrorCode.Should().Be("AUTH_EMAIL_ALREADY_REGISTERED");
        await users.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }
}
