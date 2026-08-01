using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Admin.CreateAdminUser;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.UnitTests.Application.Admin;

public sealed class CreateAdminUserCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_HappyPath_CreatesPasswordlessSystemAdminPendingInitialPassword_AndSendsInitialPasswordLink()
    {
        var handler = CreateHandler(
            out var users,
            out var tokens,
            out var activityLogs,
            out var emailService,
            out _);
        User? capturedUser = null;
        EmailVerificationToken? capturedToken = null;
        ActivityLog? capturedActivityLog = null;
        AccountCreatedEmailDto? capturedEmailInfo = null;
        var callerId = Guid.NewGuid();

        users.GetByEmailAsync("admin@example.com", Arg.Any<CancellationToken>()).Returns((User?)null);
        users.AddAsync(Arg.Do<User>(user => capturedUser = user), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<User>());
        tokens.AddAsync(Arg.Do<EmailVerificationToken>(token => capturedToken = token), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<EmailVerificationToken>());
        activityLogs.AddAsync(Arg.Do<ActivityLog>(log => capturedActivityLog = log), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ActivityLog>());
        emailService.SendAccountCreatedLinkAsync(
                "admin@example.com",
                Arg.Do<AccountCreatedEmailDto>(info => capturedEmailInfo = info),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await handler.Handle(
            new CreateAdminUserCommand(
                callerId,
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
        capturedToken.Should().NotBeNull();
        capturedToken!.UserId.Should().Be(capturedUser.Id);
        capturedToken.Purpose.Should().Be(EmailVerificationPurpose.SET_INITIAL_PASSWORD);
        capturedToken.Code.Should().Be("initial-token");
        capturedToken.ExpiresAt.Should().Be(Now.AddHours(48));
        capturedEmailInfo.Should().NotBeNull();
        capturedEmailInfo!.OperationId.Should().Be(capturedToken.Id);
        capturedEmailInfo.OperationId.ToString("D")[14].Should().Be('4');
        capturedEmailInfo.UserId.Should().Be(capturedUser.Id);
        capturedEmailInfo.DisplayName.Should().Be("Admin User");
        capturedEmailInfo.SetInitialPasswordUrl.Should().EndWith("initial-token");
        capturedEmailInfo.ExpiresAt.Should().Be(Now.AddHours(48));
        capturedActivityLog.Should().NotBeNull();
        capturedActivityLog!.UserId.Should().Be(capturedUser.Id);
        capturedActivityLog.Action.Should().Be(ActivityLogAction.SET_INITIAL_PASSWORD);
        capturedActivityLog.Metadata.Should().Contain(callerId.ToString());
    }

    [Fact]
    public async Task Handle_NonSystemAdminCaller_Throws403Forbidden()
    {
        var handler = CreateHandler(
            out var users,
            out var tokens,
            out var activityLogs,
            out var emailService,
            out _);

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
        await tokens.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await activityLogs.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await emailService.DidNotReceiveWithAnyArgs().SendAccountCreatedLinkAsync(default!, default!, default);
    }

    [Fact]
    public async Task Handle_DuplicateEmail_Throws409AuthEmailAlreadyRegistered()
    {
        var existing = User.CreateAdminPendingPassword("admin@example.com", "Existing Admin");
        var handler = CreateHandler(
            out var users,
            out var tokens,
            out var activityLogs,
            out var emailService,
            out _);

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
        await tokens.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await activityLogs.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await emailService.DidNotReceiveWithAnyArgs().SendAccountCreatedLinkAsync(default!, default!, default);
    }

    private static CreateAdminUserCommandHandler CreateHandler(
        out IUserRepository users,
        out IEmailVerificationTokenRepository tokens,
        out IActivityLogRepository activityLogs,
        out IEmailService emailService,
        out IClock clock)
    {
        users = Substitute.For<IUserRepository>();
        tokens = Substitute.For<IEmailVerificationTokenRepository>();
        activityLogs = Substitute.For<IActivityLogRepository>();
        emailService = Substitute.For<IEmailService>();
        var initialPasswordTokens = Substitute.For<IInitialPasswordTokenService>();
        initialPasswordTokens.GenerateCode().Returns("initial-token");
        initialPasswordTokens.GetExpiresAt(Now).Returns(Now.AddHours(48));
        // Stubbed for SYSTEM_ADMIN specifically rather than Arg.Any: this handler always
        // creates a system admin, so if it ever passes a different role the stub returns
        // null and the email assertions fail instead of quietly passing.
        initialPasswordTokens.BuildSetInitialPasswordUrl("initial-token", UserRole.SYSTEM_ADMIN)
            .Returns("https://test.vietride.app/auth/set-initial-password?token=initial-token");
        clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        return new CreateAdminUserCommandHandler(
            users,
            tokens,
            activityLogs,
            initialPasswordTokens,
            emailService,
            clock);
    }
}
