using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Users.CompleteProfile;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.UnitTests.Application.Users;

public sealed class CompleteProfileCommandHandlerTests
{
    [Fact]
    public async Task Handle_HappyPath_CompletesProfileAndWritesActivityLog()
    {
        var users = Substitute.For<IUserRepository>();
        var activityLogs = Substitute.For<IActivityLogRepository>();
        var user = User.CreateGoogleAccount("user@example.com", "Test User", null);
        var handler = new CompleteProfileCommandHandler(users, activityLogs);

        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        users.GetByPhoneAsync("+84901234567", Arg.Any<CancellationToken>()).Returns((User?)null);
        activityLogs.AddAsync(Arg.Any<ActivityLog>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ActivityLog>());

        var result = await handler.Handle(
            new CompleteProfileCommand(user.Id, "+84901234567"),
            CancellationToken.None);

        result.UserId.Should().Be(user.Id);
        result.Phone.Should().Be("+84901234567");
        result.Message.Should().Be("Hồ sơ hoàn tất.");
        user.Phone!.Value.Value.Should().Be("+84901234567");
        users.Received(1).Update(user);
        await activityLogs.Received(1).AddAsync(
            Arg.Is<ActivityLog>(log => log.UserId == user.Id && log.Action == ActivityLogAction.COMPLETE_PROFILE),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvalidPhone_Throws400WithAuthPhoneInvalidFormat()
    {
        var users = Substitute.For<IUserRepository>();
        var activityLogs = Substitute.For<IActivityLogRepository>();
        var user = User.CreateGoogleAccount("user@example.com", "Test User", null);
        var handler = new CompleteProfileCommandHandler(users, activityLogs);

        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var act = () => handler.Handle(
            new CompleteProfileCommand(user.Id, "0901234567"),
            CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<BadRequestException>();
        assertion.Which.ErrorCode.Should().Be("AUTH_PHONE_INVALID_FORMAT");
        await activityLogs.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Handle_DuplicatePhone_Throws409WithAuthPhoneAlreadyRegistered()
    {
        var users = Substitute.For<IUserRepository>();
        var activityLogs = Substitute.For<IActivityLogRepository>();
        var user = User.CreateGoogleAccount("user@example.com", "Test User", null);
        var existing = User.CreatePassenger(
            "other@example.com",
            PhoneNumber.Parse("+84901234567"),
            "$2a$12$hashedpassword",
            "Other User");
        var handler = new CompleteProfileCommandHandler(users, activityLogs);

        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        users.GetByPhoneAsync("+84901234567", Arg.Any<CancellationToken>()).Returns(existing);

        var act = () => handler.Handle(
            new CompleteProfileCommand(user.Id, "+84901234567"),
            CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<ConflictException>();
        assertion.Which.ErrorCode.Should().Be("AUTH_PHONE_ALREADY_REGISTERED");
        users.DidNotReceiveWithAnyArgs().Update(default!);
        await activityLogs.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Handle_WhenPhoneAlreadySet_Throws422ValidationError()
    {
        var users = Substitute.For<IUserRepository>();
        var activityLogs = Substitute.For<IActivityLogRepository>();
        var user = User.CreatePassenger(
            "user@example.com",
            PhoneNumber.Parse("+84901234567"),
            "$2a$12$hashedpassword",
            "Test User");
        var handler = new CompleteProfileCommandHandler(users, activityLogs);

        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var act = () => handler.Handle(
            new CompleteProfileCommand(user.Id, "+84907654321"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        await users.DidNotReceiveWithAnyArgs().GetByPhoneAsync(default!, default);
        await activityLogs.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }
}
