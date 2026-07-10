using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Auth.Login;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using Xunit;

namespace VietRide.Identity.UnitTests.Application.Auth;

public sealed class LoginCommandHandlerTests
{
    private static readonly DateTimeOffset FrozenNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly PhoneNumber TestPhone = PhoneNumber.Parse("+84901234567");

    private static (
        LoginCommandHandler handler,
        IUserRepository users,
        IOperatorRepository operators,
        IRefreshTokenRepository tokens,
        IAccessTokenService accessTokenService,
        IFailedLoginPersister failedLoginPersister,
        ILoginLockoutCounter lockoutCounter) CreateHandler(
            IPasswordHasher? hasher = null,
            IOperatorRepository? operators = null,
            IFailedLoginPersister? failedLoginPersister = null,
            ILoginLockoutCounter? lockoutCounter = null)
    {
        var users = Substitute.For<IUserRepository>();
        operators ??= Substitute.For<IOperatorRepository>();
        var tokens = Substitute.For<IRefreshTokenRepository>();
        hasher ??= Substitute.For<IPasswordHasher>();
        var accessTokenSvc = Substitute.For<IAccessTokenService>();
        var refreshFactory = Substitute.For<IRefreshTokenFactory>();
        failedLoginPersister ??= Substitute.For<IFailedLoginPersister>();
        lockoutCounter ??= Substitute.For<ILoginLockoutCounter>();
        var clock = Substitute.For<IClock>();

        clock.UtcNow.Returns(FrozenNow);
        hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        accessTokenSvc.IssueToken(Arg.Any<User>()).Returns("jwt.access.token");
        lockoutCounter.IncrementAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(1L);

        var (rawToken, refreshEntity) = (
            "rawtoken123",
            RefreshToken.Create(Guid.NewGuid(), "hash", Guid.NewGuid(), null, FrozenNow, FrozenNow.AddDays(30)));
        refreshFactory.Create(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<Guid?>())
            .Returns((rawToken, refreshEntity));
        tokens.AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<RefreshToken>());

        var handler = new LoginCommandHandler(
            users,
            operators,
            tokens,
            hasher,
            accessTokenSvc,
            refreshFactory,
            failedLoginPersister,
            lockoutCounter,
            clock);
        return (handler, users, operators, tokens, accessTokenSvc, failedLoginPersister, lockoutCounter);
    }

    private static User MakeActiveUser(string email = "user@example.com", string passwordHash = "stored_hash")
    {
        var user = User.CreatePassenger(email, TestPhone, passwordHash, "Test User");
        user.VerifyEmail();
        return user;
    }

    private static User MakePendingInitialPasswordUser()
    {
        var user = MakeActiveUser();
        typeof(User)
            .GetProperty(nameof(User.Status))!
            .SetValue(user, UserStatus.PENDING_INITIAL_PASSWORD);
        return user;
    }

    private static User MakeOperatorUser(UserRole role, Guid operatorId)
    {
        var user = MakeActiveUser();
        typeof(User)
            .GetProperty(nameof(User.Role))!
            .SetValue(user, role);
        typeof(User)
            .GetProperty(nameof(User.OperatorId))!
            .SetValue(user, operatorId);
        return user;
    }

    private static Operator MakeOperator(OperatorRegistrationStatus status)
    {
        var operatorEntity = (Operator)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Operator));
        typeof(Operator)
            .GetProperty(nameof(Operator.RegistrationStatus))!
            .SetValue(operatorEntity, status);
        return operatorEntity;
    }

    // -------------------------------------------------------------------------
    // Happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ValidCredentials_ReturnsTokenBundle()
    {
        var hasher = Substitute.For<IPasswordHasher>();
        var (handler, users, _, _, _, _, lockoutCounter) = CreateHandler(hasher: hasher);
        hasher.Verify("correct_password", "stored_hash").Returns(true);
        var user = MakeActiveUser();

        users.GetByEmailAsync("user@example.com", Arg.Any<CancellationToken>())
            .Returns(user);

        var result = await handler.Handle(new LoginCommand("user@example.com", "correct_password"), CancellationToken.None);

        result.Should().NotBeNull();
        result.AccessToken.Should().Be("jwt.access.token");
        result.RefreshToken.Should().Be("rawtoken123");
        result.ExpiresInSeconds.Should().Be(900);
        result.User.Email.Should().Be("user@example.com");
        result.User.Phone.Should().Be(TestPhone.Value);
        result.User.Role.Should().Be(UserRole.PASSENGER.ToString());

        await lockoutCounter.Received(1).ResetAsync(user.Id, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(UserRole.OPERATOR_ADMIN)]
    [InlineData(UserRole.OPERATOR_STAFF)]
    public async Task Handle_ApprovedOperatorAdminOrStaff_ReturnsTokenBundle(UserRole role)
    {
        var hasher = Substitute.For<IPasswordHasher>();
        var (handler, users, operators, _, _, _, lockoutCounter) = CreateHandler(hasher: hasher);
        hasher.Verify("correct_password", "stored_hash").Returns(true);
        var operatorId = Guid.NewGuid();
        var user = MakeOperatorUser(role, operatorId);
        users.GetByEmailAsync("user@example.com", Arg.Any<CancellationToken>()).Returns(user);
        operators.GetByIdAsync(operatorId, Arg.Any<CancellationToken>())
            .Returns(MakeOperator(OperatorRegistrationStatus.APPROVED));

        var result = await handler.Handle(new LoginCommand("user@example.com", "correct_password"), CancellationToken.None);

        result.AccessToken.Should().Be("jwt.access.token");
        result.User.Role.Should().Be(role.ToString());
        result.User.OperatorId.Should().Be(operatorId);
        await lockoutCounter.Received(1).ResetAsync(user.Id, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(UserRole.SYSTEM_ADMIN)]
    [InlineData(UserRole.PASSENGER)]
    [InlineData(UserRole.DRIVER)]
    public async Task Handle_NonOperatorAdminOrStaffRoles_DoNotCheckOperatorApproval(UserRole role)
    {
        var hasher = Substitute.For<IPasswordHasher>();
        var (handler, users, operators, _, _, _, _) = CreateHandler(hasher: hasher);
        hasher.Verify("correct_password", "stored_hash").Returns(true);
        var operatorId = Guid.NewGuid();
        var user = MakeOperatorUser(role, operatorId);
        users.GetByEmailAsync("user@example.com", Arg.Any<CancellationToken>()).Returns(user);

        var result = await handler.Handle(new LoginCommand("user@example.com", "correct_password"), CancellationToken.None);

        result.AccessToken.Should().Be("jwt.access.token");
        result.User.Role.Should().Be(role.ToString());
        await operators.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Error cases
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(UserRole.OPERATOR_ADMIN, OperatorRegistrationStatus.PENDING)]
    [InlineData(UserRole.OPERATOR_ADMIN, OperatorRegistrationStatus.SUSPENDED)]
    [InlineData(UserRole.OPERATOR_ADMIN, OperatorRegistrationStatus.REJECTED)]
    [InlineData(UserRole.OPERATOR_STAFF, OperatorRegistrationStatus.PENDING)]
    [InlineData(UserRole.OPERATOR_STAFF, OperatorRegistrationStatus.SUSPENDED)]
    [InlineData(UserRole.OPERATOR_STAFF, OperatorRegistrationStatus.REJECTED)]
    public async Task Handle_NonApprovedOperatorAfterValidCredentials_Throws403WithoutLoginSideEffects(
        UserRole role,
        OperatorRegistrationStatus registrationStatus)
    {
        var hasher = Substitute.For<IPasswordHasher>();
        var (handler, users, operators, tokens, accessTokenService, failedLoginPersister, lockoutCounter) =
            CreateHandler(hasher: hasher);
        hasher.Verify("correct_password", "stored_hash").Returns(true);
        var operatorId = Guid.NewGuid();
        var user = MakeOperatorUser(role, operatorId);
        var originalLastLoginAt = user.LastLoginAt;
        users.GetByEmailAsync("user@example.com", Arg.Any<CancellationToken>()).Returns(user);
        operators.GetByIdAsync(operatorId, Arg.Any<CancellationToken>()).Returns(MakeOperator(registrationStatus));

        var act = () => handler.Handle(new LoginCommand("user@example.com", "correct_password"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .Where(e => e.ErrorCode == "FORBIDDEN");

        hasher.Received(1).Verify("correct_password", "stored_hash");
        user.LastLoginAt.Should().Be(originalLastLoginAt);
        accessTokenService.DidNotReceive().IssueToken(Arg.Any<User>());
        await tokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
        await lockoutCounter.DidNotReceive().ResetAsync(user.Id, Arg.Any<CancellationToken>());
        await lockoutCounter.DidNotReceive().IncrementAsync(user.Id, Arg.Any<CancellationToken>());
        await failedLoginPersister.DidNotReceive().PersistAsync(user.Id, Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WrongPassword_Throws401()
    {
        var (handler, users, _, _, _, failedLoginPersister, lockoutCounter) = CreateHandler();
        var user = MakeActiveUser();
        users.GetByEmailAsync("user@example.com", Arg.Any<CancellationToken>()).Returns(user);
        lockoutCounter.IncrementAsync(user.Id, Arg.Any<CancellationToken>()).Returns(1L);
        // hasher.Verify returns false by default in CreateHandler.

        var act = () => handler.Handle(new LoginCommand("user@example.com", "wrong_password"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .Where(e => e.ErrorCode == "AUTH_INVALID_CREDENTIALS");

        await lockoutCounter.Received(1).IncrementAsync(user.Id, Arg.Any<CancellationToken>());
        await failedLoginPersister.Received(1).PersistAsync(user.Id, 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FifthWrongPasswordInRedisWindow_LocksAccount()
    {
        var (handler, users, _, _, _, failedLoginPersister, lockoutCounter) = CreateHandler();
        var user = MakeActiveUser();
        users.GetByEmailAsync("user@example.com", Arg.Any<CancellationToken>()).Returns(user);
        lockoutCounter.IncrementAsync(user.Id, Arg.Any<CancellationToken>()).Returns(5L);

        var act = () => handler.Handle(new LoginCommand("user@example.com", "wrong_password"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .Where(e => e.ErrorCode == "AUTH_INVALID_CREDENTIALS");

        await failedLoginPersister.Received(1).PersistAsync(user.Id, 5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PendingPassengerEmailWithValidCredentials_ReturnsTokenBundle()
    {
        var hasher = Substitute.For<IPasswordHasher>();
        var (handler, users, _, _, _, _, lockoutCounter) = CreateHandler(hasher: hasher);
        hasher.Verify("correct_password", "hash").Returns(true);
        var user = User.CreatePassenger("user@example.com", TestPhone, "hash", "User");

        users.GetByEmailAsync("user@example.com", Arg.Any<CancellationToken>()).Returns(user);

        var result = await handler.Handle(new LoginCommand("user@example.com", "correct_password"), CancellationToken.None);

        result.AccessToken.Should().Be("jwt.access.token");
        result.RefreshToken.Should().Be("rawtoken123");
        result.User.Status.Should().Be(UserStatus.PENDING_EMAIL_VERIFICATION.ToString());
        await lockoutCounter.Received(1).ResetAsync(user.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PendingPassengerEmailWithWrongPassword_Throws401()
    {
        var (handler, users, _, _, _, failedLoginPersister, lockoutCounter) = CreateHandler();
        var user = User.CreatePassenger("user@example.com", TestPhone, "hash", "User");
        users.GetByEmailAsync("user@example.com", Arg.Any<CancellationToken>()).Returns(user);

        var act = () => handler.Handle(new LoginCommand("user@example.com", "wrong_password"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .Where(e => e.ErrorCode == "AUTH_INVALID_CREDENTIALS");

        await lockoutCounter.Received(1).IncrementAsync(user.Id, Arg.Any<CancellationToken>());
        await failedLoginPersister.Received(1).PersistAsync(user.Id, 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PendingNonPassengerEmail_Throws403()
    {
        var (handler, users, _, _, _, _, lockoutCounter) = CreateHandler();
        var user = MakeOperatorUser(UserRole.OPERATOR_ADMIN, Guid.NewGuid());
        typeof(User)
            .GetProperty(nameof(User.Status))!
            .SetValue(user, UserStatus.PENDING_EMAIL_VERIFICATION);

        users.GetByEmailAsync("user@example.com", Arg.Any<CancellationToken>()).Returns(user);

        var act = () => handler.Handle(new LoginCommand("user@example.com", "any_pass"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .Where(e => e.ErrorCode == "AUTH_EMAIL_NOT_VERIFIED");

        await lockoutCounter.DidNotReceive().IncrementAsync(user.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LockedAccount_Throws403()
    {
        var (handler, users, _, _, _, _, _) = CreateHandler();
        var user = MakeActiveUser();
        user.Lock();

        users.GetByEmailAsync("user@example.com", Arg.Any<CancellationToken>()).Returns(user);

        var act = () => handler.Handle(new LoginCommand("user@example.com", "any_pass"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .Where(e => e.ErrorCode == "AUTH_ACCOUNT_LOCKED");
    }

    [Fact]
    public async Task Handle_PendingInitialPassword_Throws403()
    {
        var (handler, users, _, _, _, _, lockoutCounter) = CreateHandler();
        var user = MakePendingInitialPasswordUser();

        users.GetByEmailAsync("user@example.com", Arg.Any<CancellationToken>()).Returns(user);

        var act = () => handler.Handle(new LoginCommand("user@example.com", "any_pass"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .Where(e => e.ErrorCode == "AUTH_PENDING_INITIAL_PASSWORD");

        await lockoutCounter.DidNotReceive().IncrementAsync(user.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UserNotFound_Throws401()
    {
        var (handler, users, _, _, _, _, lockoutCounter) = CreateHandler();
        users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var act = () => handler.Handle(new LoginCommand("nobody@example.com", "pass"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>()
            .Where(e => e.ErrorCode == "AUTH_INVALID_CREDENTIALS");

        await lockoutCounter.DidNotReceive().IncrementAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
