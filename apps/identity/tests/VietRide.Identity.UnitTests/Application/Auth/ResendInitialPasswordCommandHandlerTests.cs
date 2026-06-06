using FluentAssertions;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Auth.ResendInitialPassword;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Domain.Exceptions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.UnitTests.Application.Auth;

public sealed class ResendInitialPasswordCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_HappyPath_RevokesOldToken_GeneratesFreshToken_SendsEmail_AndLogsActivity()
    {
        var operatorId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var user = CreateOperatorPendingUser(operatorId);
        var oldToken = EmailVerificationToken.Create(
            user.Id,
            EmailVerificationPurpose.SET_INITIAL_PASSWORD,
            "old-token",
            Now.AddHours(1));

        var sut = CreateHandler(
            [user],
            [oldToken],
            out var tokens,
            out var activityLogs,
            out var emailService);

        var result = await sut.Handle(
            new ResendInitialPasswordCommand(
                user.Id,
                callerId,
                UserRole.OPERATOR_ADMIN.ToString(),
                operatorId),
            CancellationToken.None);

        result.UserId.Should().Be(user.Id);
        result.Status.Should().Be(UserStatus.PENDING_INITIAL_PASSWORD.ToString());
        result.ExpiresAt.Should().Be(Now.AddHours(48));
        oldToken.UsedAt.Should().Be(Now);
        tokens.Entities.Should().HaveCount(2);
        tokens.Entities.Should().ContainSingle(t => t.Code == "new-token" && t.UsedAt == null);
        emailService.Sent.Should().ContainSingle();
        emailService.Sent[0].To.Should().Be(user.Email);
        emailService.Sent[0].Info.SetInitialPasswordUrl.Should().EndWith("new-token");
        activityLogs.Entities.Should().ContainSingle(l => l.Action == ActivityLogAction.RESEND_INITIAL_PASSWORD);
    }

    [Fact]
    public async Task Handle_HappyPath_Succeeds_WhenNoPriorActiveTokenExists()
    {
        var operatorId = Guid.NewGuid();
        var user = CreateOperatorPendingUser(operatorId);
        var sut = CreateHandler([user], [], out var tokens, out _, out _);

        await sut.Handle(
            new ResendInitialPasswordCommand(
                user.Id,
                Guid.NewGuid(),
                UserRole.OPERATOR_ADMIN.ToString(),
                operatorId),
            CancellationToken.None);

        tokens.Entities.Should().ContainSingle(t => t.Code == "new-token" && t.UsedAt == null);
    }

    [Fact]
    public async Task Handle_TargetNotFound_ThrowsNotFoundException()
    {
        var sut = CreateHandler([], [], out _, out _, out _);

        var act = () => sut.Handle(
            new ResendInitialPasswordCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                UserRole.OPERATOR_ADMIN.ToString(),
                Guid.NewGuid()),
            CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_TargetNotPendingInitialPassword_ThrowsInvalidStatusTransition()
    {
        var operatorId = Guid.NewGuid();
        var user = CreateOperatorPendingUser(operatorId);
        user.SetInitialPassword("hashed-password");
        var sut = CreateHandler([user], [], out _, out _, out _);

        var act = () => sut.Handle(
            new ResendInitialPasswordCommand(
                user.Id,
                Guid.NewGuid(),
                UserRole.OPERATOR_ADMIN.ToString(),
                operatorId),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<IdentityDomainException>();
        ex.Which.ErrorCode.Should().Be("USER_INVALID_STATUS_TRANSITION");
    }

    [Fact]
    public async Task Handle_WrongCallerRole_ThrowsForbidden()
    {
        var user = CreateOperatorPendingUser(Guid.NewGuid());
        var sut = CreateHandler([user], [], out _, out _, out _);

        var act = () => sut.Handle(
            new ResendInitialPasswordCommand(
                user.Id,
                Guid.NewGuid(),
                UserRole.OPERATOR_STAFF.ToString(),
                user.OperatorId),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ForbiddenException>();
        ex.Which.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task Handle_CrossOperatorTarget_ThrowsForbidden()
    {
        var user = CreateOperatorPendingUser(Guid.NewGuid());
        var sut = CreateHandler([user], [], out _, out _, out _);

        var act = () => sut.Handle(
            new ResendInitialPasswordCommand(
                user.Id,
                Guid.NewGuid(),
                UserRole.OPERATOR_ADMIN.ToString(),
                Guid.NewGuid()),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ForbiddenException>();
        ex.Which.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task Handle_NonOperatorTarget_ThrowsForbidden()
    {
        var user = User.CreateAdminPendingPassword("system-admin@example.com", "System Admin");
        var sut = CreateHandler([user], [], out _, out _, out _);

        var act = () => sut.Handle(
            new ResendInitialPasswordCommand(
                user.Id,
                Guid.NewGuid(),
                UserRole.OPERATOR_ADMIN.ToString(),
                Guid.NewGuid()),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ForbiddenException>();
        ex.Which.ErrorCode.Should().Be("FORBIDDEN");
    }

    private static ResendInitialPasswordCommandHandler CreateHandler(
        IReadOnlyCollection<User> users,
        IReadOnlyCollection<EmailVerificationToken> initialTokens,
        out FakeEmailVerificationTokenRepository tokens,
        out FakeActivityLogRepository activityLogs,
        out CapturingEmailService emailService)
    {
        var userRepository = new FakeUserRepository(users);
        tokens = new FakeEmailVerificationTokenRepository(initialTokens);
        activityLogs = new FakeActivityLogRepository();
        emailService = new CapturingEmailService();

        return new ResendInitialPasswordCommandHandler(
            userRepository,
            tokens,
            activityLogs,
            new FixedInitialPasswordTokenService(),
            emailService,
            new FixedClock());
    }

    private static User CreateOperatorPendingUser(Guid operatorId)
    {
        var user = User.CreateAdminPendingPassword("driver@example.com", "Driver One");
        SetPrivateProperty(user, nameof(User.Role), UserRole.DRIVER);
        SetPrivateProperty(user, nameof(User.OperatorId), operatorId);
        return user;
    }

    private static void SetPrivateProperty<T>(User user, string propertyName, T value)
        => typeof(User).GetProperty(propertyName)!.SetValue(user, value);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FixedInitialPasswordTokenService : IInitialPasswordTokenService
    {
        public string GenerateCode() => "new-token";

        public DateTimeOffset GetExpiresAt(DateTimeOffset now) => now.AddHours(48);
    }

    private sealed class CapturingEmailService : IEmailService
    {
        public List<(string To, AccountCreatedEmailDto Info)> Sent { get; } = [];

        public Task SendOtpAsync(string to, string code, EmailOtpPurpose purpose, int ttlMinutes, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task SendAccountCreatedLinkAsync(
            string to,
            AccountCreatedEmailDto accountInfo,
            CancellationToken ct = default)
        {
            Sent.Add((to, accountInfo));
            return Task.CompletedTask;
        }

        public Task SendParcelDeliveryLinkAsync(
            string to,
            string deliveryToken,
            ParcelDeliveryEmailDto parcelInfo,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        private readonly List<User> _users;

        public FakeUserRepository(IEnumerable<User> users)
        {
            _users = users.ToList();
        }

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_users.FirstOrDefault(u => u.Id == id));

        public Task<User?> GetByEmailAsync(string emailLower, CancellationToken ct = default)
            => Task.FromResult(_users.FirstOrDefault(u => u.Email == emailLower));

        public Task<User?> GetByPhoneAsync(string e164Phone, CancellationToken ct = default)
            => Task.FromResult<User?>(null);

        public Task<User> AddAsync(User entity, CancellationToken ct)
        {
            _users.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(User entity) { }

        public void Remove(User entity) => _users.Remove(entity);

        public IQueryable<User> Query() => _users.AsQueryable();

        public IQueryable<User> QueryNoTracking() => _users.AsQueryable();
    }

    private sealed class FakeEmailVerificationTokenRepository : IEmailVerificationTokenRepository
    {
        public FakeEmailVerificationTokenRepository(IEnumerable<EmailVerificationToken> tokens)
        {
            Entities = tokens.ToList();
        }

        public List<EmailVerificationToken> Entities { get; }

        public Task<EmailVerificationToken?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Entities.FirstOrDefault(t => t.Id == id));

        public Task<EmailVerificationToken?> FindActiveAsync(
            Guid userId,
            string code,
            EmailVerificationPurpose purpose,
            DateTimeOffset now,
            CancellationToken ct = default)
            => Task.FromResult(Entities.FirstOrDefault(t =>
                t.UserId == userId && t.Code == code && t.Purpose == purpose && t.UsedAt is null && t.ExpiresAt > now));

        public Task<EmailVerificationToken?> FindByCodeAsync(
            Guid userId,
            string code,
            EmailVerificationPurpose purpose,
            CancellationToken ct = default)
            => Task.FromResult(Entities.FirstOrDefault(t =>
                t.UserId == userId && t.Code == code && t.Purpose == purpose && t.UsedAt is null));

        public Task<EmailVerificationToken?> FindByCodeAndPurposeAsync(
            string code,
            EmailVerificationPurpose purpose,
            CancellationToken ct = default)
            => Task.FromResult(Entities.FirstOrDefault(t => t.Code == code && t.Purpose == purpose && t.UsedAt is null));

        public Task<EmailVerificationToken?> FindLatestPendingAsync(
            Guid userId,
            EmailVerificationPurpose purpose,
            CancellationToken ct = default)
            => Task.FromResult(Entities.Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAt is null)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefault());

        public Task RevokeActiveByUserAndPurposeAsync(
            Guid userId,
            EmailVerificationPurpose purpose,
            DateTimeOffset revokedAt,
            CancellationToken ct = default)
        {
            foreach (var token in Entities.Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAt is null))
                token.MarkUsed(revokedAt);

            return Task.CompletedTask;
        }

        public Task<bool> TryAddAsync(EmailVerificationToken entity, CancellationToken ct = default)
        {
            Entities.Add(entity);
            return Task.FromResult(true);
        }

        public Task<EmailVerificationToken> AddAsync(EmailVerificationToken entity, CancellationToken ct)
        {
            Entities.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(EmailVerificationToken entity) { }

        public void Remove(EmailVerificationToken entity) => Entities.Remove(entity);

        public IQueryable<EmailVerificationToken> Query() => Entities.AsQueryable();

        public IQueryable<EmailVerificationToken> QueryNoTracking() => Entities.AsQueryable();
    }

    private sealed class FakeActivityLogRepository : IActivityLogRepository
    {
        public List<ActivityLog> Entities { get; } = [];

        public Task<ActivityLog?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Entities.FirstOrDefault(l => l.Id == id));

        public Task<ActivityLog> AddAsync(ActivityLog entity, CancellationToken ct)
        {
            Entities.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(ActivityLog entity) { }

        public void Remove(ActivityLog entity) => Entities.Remove(entity);

        public IQueryable<ActivityLog> Query() => Entities.AsQueryable();

        public IQueryable<ActivityLog> QueryNoTracking() => Entities.AsQueryable();
    }
}
