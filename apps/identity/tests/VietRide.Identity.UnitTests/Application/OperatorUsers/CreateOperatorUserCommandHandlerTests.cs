using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.OperatorUsers.CreateOperatorUser;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.UnitTests.Application.OperatorUsers;

public sealed class CreateOperatorUserCommandHandlerTests
{
    private static readonly Guid CallerUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OperatorId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAt = Now.AddHours(48);

    [Fact]
    public async Task Handle_ApprovedOperatorAdminCreatesDriver_PersistsPendingUserTokenAndActivityLog_AndSendsEmail()
    {
        var users = new FakeUserRepository();
        var operators = new FakeOperatorRepository(CreateOperator(OperatorRegistrationStatus.APPROVED));
        var subscriptions = new FakeOperatorSubscriptionRepository(true);
        var emailService = Substitute.For<IEmailService>();
        var usageWarnings = Substitute.For<ISubscriptionUsageWarningPublisher>();
        var handler = CreateHandler(users, operators, subscriptions, emailService, usageWarnings: usageWarnings);

        var response = await handler.Handle(CreateCommand(UserRole.DRIVER), CancellationToken.None);

        response.Email.Should().Be("driver@example.com");
        response.Phone.Should().Be("+84901112222");
        response.DisplayName.Should().Be("Driver One");
        response.Role.Should().Be(UserRole.DRIVER.ToString());
        response.Status.Should().Be(UserStatus.PENDING_INITIAL_PASSWORD.ToString());
        response.OperatorId.Should().Be(OperatorId);
        response.InitialPasswordExpiresAt.Should().Be(ExpiresAt);

        subscriptions.Calls.Should().Be(1);
        subscriptions.CapturedUser.Should().NotBeNull();
        subscriptions.CapturedUser!.PasswordHash.Should().BeNull();
        subscriptions.CapturedUser.Status.Should().Be(UserStatus.PENDING_INITIAL_PASSWORD);
        subscriptions.CapturedUser.OperatorId.Should().Be(OperatorId);
        subscriptions.CapturedToken.Should().NotBeNull();
        subscriptions.CapturedToken!.Purpose.Should().Be(EmailVerificationPurpose.SET_INITIAL_PASSWORD);
        subscriptions.CapturedActivityLog.Should().NotBeNull();
        subscriptions.CapturedActivityLog!.UserId.Should().Be(CallerUserId);
        subscriptions.CapturedActivityLog.Action.Should().Be(ActivityLogAction.SET_INITIAL_PASSWORD);
        subscriptions.CapturedActivityLog.Metadata.Should().Contain("OPERATOR_USER_CREATE");
        await usageWarnings.Received(1).EnqueueIfThresholdCrossedAsync(
            Arg.Any<OperatorSubscription>(),
            Arg.Any<SubscriptionPlan>(),
            SubscriptionUsageResource.DRIVERS,
            1,
            null,
            Arg.Any<CancellationToken>());

        await emailService.Received(1).SendAccountCreatedLinkAsync(
            "driver@example.com",
            Arg.Is<AccountCreatedEmailDto>(dto =>
                dto.UserId == response.UserId &&
                dto.DisplayName == "Driver One" &&
                dto.SetInitialPasswordUrl.EndsWith("initial-code", StringComparison.Ordinal) &&
                dto.ExpiresAt == ExpiresAt),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(UserRole.DRIVER)]
    [InlineData(UserRole.ASSISTANT)]
    [InlineData(UserRole.OPERATOR_STAFF)]
    public async Task Handle_AllowedRoles_PassesRoleToSubscriptionCounter(UserRole role)
    {
        var users = new FakeUserRepository();
        var operators = new FakeOperatorRepository(CreateOperator(OperatorRegistrationStatus.APPROVED));
        var subscriptions = new FakeOperatorSubscriptionRepository(true);
        var emailService = Substitute.For<IEmailService>();
        var handler = CreateHandler(users, operators, subscriptions, emailService);

        await handler.Handle(CreateCommand(role), CancellationToken.None);

        subscriptions.CapturedRole.Should().Be(role);
    }

    // The role decides which landing page the emailed link points at: DRIVER/ASSISTANT
    // must reach the mobile "open in app" page, OPERATOR_STAFF the operator web form.
    // Passing the wrong role here silently sends someone to a page they cannot use.
    [Theory]
    [InlineData(UserRole.DRIVER)]
    [InlineData(UserRole.ASSISTANT)]
    [InlineData(UserRole.OPERATOR_STAFF)]
    public async Task Handle_AllowedRoles_PassesCreatedRoleToUrlBuilder(UserRole role)
    {
        var users = new FakeUserRepository();
        var operators = new FakeOperatorRepository(CreateOperator(OperatorRegistrationStatus.APPROVED));
        var subscriptions = new FakeOperatorSubscriptionRepository(true);
        var emailService = Substitute.For<IEmailService>();
        var tokens = Substitute.For<IInitialPasswordTokenService>();
        var handler = CreateHandler(users, operators, subscriptions, emailService, tokens);

        await handler.Handle(CreateCommand(role), CancellationToken.None);

        tokens.Received(1).BuildSetInitialPasswordUrl("initial-code", role);
    }

    [Fact]
    public async Task Handle_NonAdminCaller_ReturnsForbiddenBeforeUserCounterTokenOrEmailSideEffects()
    {
        var users = new FakeUserRepository();
        var operators = new FakeOperatorRepository(CreateOperator(OperatorRegistrationStatus.APPROVED));
        var subscriptions = new FakeOperatorSubscriptionRepository(true);
        var emailService = Substitute.For<IEmailService>();
        var handler = CreateHandler(users, operators, subscriptions, emailService);

        var act = () => handler.Handle(
            CreateCommand(UserRole.DRIVER, UserRole.OPERATOR_STAFF.ToString(), OperatorId),
            CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<ForbiddenException>();
        assertion.Which.ErrorCode.Should().Be("FORBIDDEN");
        users.EmailLookups.Should().Be(0);
        users.PhoneLookups.Should().Be(0);
        subscriptions.Calls.Should().Be(0);
        await emailService.DidNotReceiveWithAnyArgs().SendAccountCreatedLinkAsync(default!, default!, default);
    }

    [Fact]
    public async Task Handle_MissingOperatorId_ReturnsForbiddenBeforeUserCounterTokenOrEmailSideEffects()
    {
        var users = new FakeUserRepository();
        var operators = new FakeOperatorRepository(CreateOperator(OperatorRegistrationStatus.APPROVED));
        var subscriptions = new FakeOperatorSubscriptionRepository(true);
        var emailService = Substitute.For<IEmailService>();
        var handler = CreateHandler(users, operators, subscriptions, emailService);

        var act = () => handler.Handle(
            CreateCommand(UserRole.DRIVER, UserRole.OPERATOR_ADMIN.ToString(), operatorId: null),
            CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<ForbiddenException>();
        assertion.Which.ErrorCode.Should().Be("FORBIDDEN");
        users.EmailLookups.Should().Be(0);
        users.PhoneLookups.Should().Be(0);
        subscriptions.Calls.Should().Be(0);
        await emailService.DidNotReceiveWithAnyArgs().SendAccountCreatedLinkAsync(default!, default!, default);
    }

    [Theory]
    [InlineData(OperatorRegistrationStatus.SUSPENDED)]
    [InlineData(OperatorRegistrationStatus.REJECTED)]
    public async Task Handle_NonApprovedOperator_ReturnsForbiddenBeforeUserCounterTokenOrEmailSideEffects(
        OperatorRegistrationStatus status)
    {
        var users = new FakeUserRepository();
        var operators = new FakeOperatorRepository(CreateOperator(status));
        var subscriptions = new FakeOperatorSubscriptionRepository(true);
        var emailService = Substitute.For<IEmailService>();
        var handler = CreateHandler(users, operators, subscriptions, emailService);

        var act = () => handler.Handle(CreateCommand(UserRole.DRIVER), CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<ForbiddenException>();
        assertion.Which.ErrorCode.Should().Be("FORBIDDEN");
        users.EmailLookups.Should().Be(0);
        users.PhoneLookups.Should().Be(0);
        subscriptions.Calls.Should().Be(0);
        await emailService.DidNotReceiveWithAnyArgs().SendAccountCreatedLinkAsync(default!, default!, default);
    }

    [Fact]
    public async Task Handle_DuplicateEmail_ThrowsAuthEmailAlreadyRegisteredBeforeCounterOrEmailSideEffects()
    {
        var existing = User.CreatePassenger(
            "driver@example.com",
            PhoneNumber.Parse("+84909998888"),
            "$2a$12$hashedpassword",
            "Existing User");
        var users = new FakeUserRepository(existingEmail: existing);
        var operators = new FakeOperatorRepository(CreateOperator(OperatorRegistrationStatus.APPROVED));
        var subscriptions = new FakeOperatorSubscriptionRepository(true);
        var emailService = Substitute.For<IEmailService>();
        var handler = CreateHandler(users, operators, subscriptions, emailService);

        var act = () => handler.Handle(CreateCommand(UserRole.DRIVER), CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<ConflictException>();
        assertion.Which.ErrorCode.Should().Be("AUTH_EMAIL_ALREADY_REGISTERED");
        subscriptions.Calls.Should().Be(0);
        await emailService.DidNotReceiveWithAnyArgs().SendAccountCreatedLinkAsync(default!, default!, default);
    }

    [Fact]
    public async Task Handle_DuplicatePhone_ThrowsAuthPhoneAlreadyRegisteredBeforeCounterOrEmailSideEffects()
    {
        var existing = User.CreatePassenger(
            "other@example.com",
            PhoneNumber.Parse("+84901112222"),
            "$2a$12$hashedpassword",
            "Existing User");
        var users = new FakeUserRepository(existingPhone: existing);
        var operators = new FakeOperatorRepository(CreateOperator(OperatorRegistrationStatus.APPROVED));
        var subscriptions = new FakeOperatorSubscriptionRepository(true);
        var emailService = Substitute.For<IEmailService>();
        var handler = CreateHandler(users, operators, subscriptions, emailService);

        var act = () => handler.Handle(CreateCommand(UserRole.DRIVER), CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<ConflictException>();
        assertion.Which.ErrorCode.Should().Be("AUTH_PHONE_ALREADY_REGISTERED");
        subscriptions.Calls.Should().Be(0);
        await emailService.DidNotReceiveWithAnyArgs().SendAccountCreatedLinkAsync(default!, default!, default);
    }

    [Fact]
    public async Task Handle_SubscriptionLimitExceeded_ThrowsWithoutSendingEmail()
    {
        var users = new FakeUserRepository();
        var operators = new FakeOperatorRepository(CreateOperator(OperatorRegistrationStatus.APPROVED));
        var subscriptions = new FakeOperatorSubscriptionRepository(false);
        var emailService = Substitute.For<IEmailService>();
        var handler = CreateHandler(users, operators, subscriptions, emailService);

        var act = () => handler.Handle(CreateCommand(UserRole.ASSISTANT), CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<Identity.Domain.Exceptions.IdentityDomainException>();
        assertion.Which.ErrorCode.Should().Be("SUBSCRIPTION_LIMIT_EXCEEDED");
        subscriptions.Calls.Should().Be(1);
        await emailService.DidNotReceiveWithAnyArgs().SendAccountCreatedLinkAsync(default!, default!, default);
    }

    private static CreateOperatorUserCommandHandler CreateHandler(
        IUserRepository users,
        IOperatorRepository operators,
        IOperatorSubscriptionRepository subscriptions,
        IEmailService emailService,
        IInitialPasswordTokenService? initialPasswordTokens = null,
        ISubscriptionUsageWarningPublisher? usageWarnings = null)
    {
        var tokens = initialPasswordTokens ?? Substitute.For<IInitialPasswordTokenService>();
        tokens.GenerateCode().Returns("initial-code");
        tokens.GetExpiresAt(Now).Returns(ExpiresAt);
        // Arg.Any here because this handler legitimately creates DRIVER, ASSISTANT and
        // OPERATOR_STAFF. Which role actually reaches the URL builder is asserted
        // separately in Handle_AllowedRoles_PassesCreatedRoleToUrlBuilder.
        tokens.BuildSetInitialPasswordUrl("initial-code", Arg.Any<UserRole>())
            .Returns("https://test.vietride.app/auth/set-initial-password?token=initial-code");
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        return new CreateOperatorUserCommandHandler(
            users,
            operators,
            subscriptions,
            tokens,
            emailService,
            clock,
            usageWarnings ?? Substitute.For<ISubscriptionUsageWarningPublisher>());
    }

    private static CreateOperatorUserCommand CreateCommand(UserRole role)
        => CreateCommand(role, UserRole.OPERATOR_ADMIN.ToString(), OperatorId);

    private static CreateOperatorUserCommand CreateCommand(UserRole role, string callerRole, Guid? operatorId)
        => new(
            "  Driver@Example.COM  ",
            "+84901112222",
            " Driver One ",
            role.ToString(),
            CallerUserId,
            callerRole,
            operatorId);

    private static Operator CreateOperator(OperatorRegistrationStatus status)
    {
        var operatorEntity = (Operator)Activator.CreateInstance(typeof(Operator), nonPublic: true)!;
        SetPrivateProperty(operatorEntity, nameof(Operator.Id), OperatorId);
        SetPrivateProperty(operatorEntity, nameof(Operator.RegistrationStatus), status);
        return operatorEntity;
    }

    private static void SetPrivateProperty<T>(object entity, string propertyName, T value)
    {
        var property = entity.GetType().GetProperty(propertyName)!;
        property.SetValue(entity, value);
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        private readonly User? _existingEmail;
        private readonly User? _existingPhone;

        public FakeUserRepository(User? existingEmail = null, User? existingPhone = null)
        {
            _existingEmail = existingEmail;
            _existingPhone = existingPhone;
        }

        public int EmailLookups { get; private set; }
        public int PhoneLookups { get; private set; }

        public Task<User?> GetByEmailAsync(string emailLower, CancellationToken ct = default)
        {
            EmailLookups++;
            return Task.FromResult(_existingEmail);
        }

        public Task<User?> GetByPhoneAsync(string e164Phone, CancellationToken ct = default)
        {
            PhoneLookups++;
            return Task.FromResult(_existingPhone);
        }

        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);
        public Task<User> AddAsync(User entity, CancellationToken cancellationToken = default) => Task.FromResult(entity);
        public void Update(User entity) { }
        public void Remove(User entity) { }
        public IQueryable<User> Query() => Array.Empty<User>().AsQueryable();
        public IQueryable<User> QueryNoTracking() => Array.Empty<User>().AsQueryable();
        public Task<PagedResult<User>> ListOperatorUsersAsync(
            QueryOptions options,
            Guid? operatorId,
            UserRole? role,
            UserStatus? status,
            CancellationToken ct = default)
            => Task.FromResult(PagedResult<User>.Create([], 1, 20, 0));

        public Task<IReadOnlyList<Guid>> ListActiveOperatorAdminIdsAsync(
            Guid operatorId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([]);
    }

    private sealed class FakeOperatorRepository : IOperatorRepository
    {
        private readonly Operator? _operator;

        public FakeOperatorRepository(Operator? operatorEntity)
        {
            _operator = operatorEntity;
        }

        public Task<Operator?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_operator);
        public Task<Operator?> GetByBusinessRegistrationNumberAsync(string businessRegistrationNumber, CancellationToken cancellationToken = default) => Task.FromResult<Operator?>(null);
        public Task<Operator?> GetByTaxCodeAsync(string taxCode, CancellationToken cancellationToken = default) => Task.FromResult<Operator?>(null);
        public Task<PagedResult<Operator>> ListAsync(QueryOptions options, OperatorRegistrationStatus? status, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Operator> AddAsync(Operator entity, CancellationToken cancellationToken = default) => Task.FromResult(entity);
        public void Update(Operator entity) { }
        public void Remove(Operator entity) { }
        public IQueryable<Operator> Query() => Array.Empty<Operator>().AsQueryable();
        public IQueryable<Operator> QueryNoTracking() => Array.Empty<Operator>().AsQueryable();
    }

    private sealed class FakeOperatorSubscriptionRepository : IOperatorSubscriptionRepository
    {
        private readonly bool _canCreate;

        public FakeOperatorSubscriptionRepository(bool canCreate)
        {
            _canCreate = canCreate;
        }

        public int Calls { get; private set; }
        public UserRole? CapturedRole { get; private set; }
        public User? CapturedUser { get; private set; }
        public EmailVerificationToken? CapturedToken { get; private set; }
        public ActivityLog? CapturedActivityLog { get; private set; }

        public Task<bool> TryCreateOperatorUserWithinLimitAsync(
            Guid operatorId,
            User user,
            EmailVerificationToken initialPasswordToken,
            ActivityLog activityLog,
            UserRole role,
            DateTimeOffset decisionAt,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            CapturedRole = role;
            CapturedUser = user;
            CapturedToken = initialPasswordToken;
            CapturedActivityLog = activityLog;
            return Task.FromResult(_canCreate);
        }

        public Task<(OperatorSubscription Subscription, SubscriptionPlan Plan)?> GetCurrentWithPlanByOperatorIdAsync(
            Guid operatorId,
            CancellationToken cancellationToken = default)
        {
            if (!_canCreate)
                return Task.FromResult<(OperatorSubscription, SubscriptionPlan)?>(null);

            var plan = SubscriptionPlan.CreateStarter();
            var subscription = OperatorSubscription.CreatePendingApproval(operatorId, plan.Id, Now);
            return Task.FromResult<(OperatorSubscription, SubscriptionPlan)?>((subscription, plan));
        }

        public Task<OperatorSubscription?> GetCurrentByOperatorIdAsync(Guid operatorId, CancellationToken cancellationToken = default) => Task.FromResult<OperatorSubscription?>(null);
        public Task<OperatorSubscription?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<OperatorSubscription?>(null);
        public Task<OperatorSubscription> AddAsync(OperatorSubscription entity, CancellationToken cancellationToken = default) => Task.FromResult(entity);
        public void Update(OperatorSubscription entity) { }
        public void Remove(OperatorSubscription entity) { }
        public IQueryable<OperatorSubscription> Query() => Array.Empty<OperatorSubscription>().AsQueryable();
        public IQueryable<OperatorSubscription> QueryNoTracking() => Array.Empty<OperatorSubscription>().AsQueryable();
    }
}
