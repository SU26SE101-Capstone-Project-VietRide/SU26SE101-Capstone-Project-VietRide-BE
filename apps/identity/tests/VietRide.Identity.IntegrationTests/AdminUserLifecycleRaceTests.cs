using System.Collections.Concurrent;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Features.Admin.LockUser;
using VietRide.Identity.Application.Features.Admin.UnlockUser;
using VietRide.Identity.Application.Features.Auth.ForgotPassword;
using VietRide.Identity.Application.Features.Auth.Login;
using VietRide.Identity.Application.Features.Auth.ResetPassword;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure;
using VietRide.Identity.IntegrationTests.Api;

namespace VietRide.Identity.IntegrationTests;

public sealed class AdminUserLifecycleRaceTests
{
    private const int Iterations = 50;
    private const string OldPassword = "OldPassword123";
    private const string NewPassword = "NewPassword123";

    [Fact]
    public async Task UserLifecycleRaces_AreLinearizedAcrossFiftyIterationsPerCase()
    {
        using var factory = new RaceAdminUsersFactory();
        try
        {
            await factory.InitializeAsync();
            var adminId = await SeedAdminAsync(factory);

            for (var iteration = 0; iteration < Iterations; iteration++)
                await AssertLockVsSuccessfulLoginAsync(factory, adminId, iteration);

            for (var iteration = 0; iteration < Iterations; iteration++)
                await AssertLockVsFailedLoginAsync(factory, adminId, 1000 + iteration);

            for (var iteration = 0; iteration < Iterations; iteration++)
                await AssertUnlockVsFailedLoginAsync(factory, adminId, 2000 + iteration);

            for (var iteration = 0; iteration < Iterations; iteration++)
                await AssertLockVsForgotPasswordAsync(factory, adminId, 3000 + iteration);

            for (var iteration = 0; iteration < Iterations; iteration++)
                await AssertLockVsResetPasswordAsync(factory, adminId, 4000 + iteration);
        }
        finally
        {
            await factory.DropDatabaseAsync();
        }
    }

    private static async Task AssertLockVsSuccessfulLoginAsync(
        RaceAdminUsersFactory factory,
        Guid adminId,
        int seed)
    {
        var user = await SeedActivePassengerAsync(factory, seed);
        var (lockResult, loginResult) = await RunTogetherAsync(
            factory,
            new LockUserCommand(adminId, UserRole.SYSTEM_ADMIN.ToString(), user.Id, null, null),
            new LoginCommand(user.Email, OldPassword));

        lockResult.Exception.Should().BeNull();
        (loginResult.Exception is null
            || loginResult.Exception is VietRide.Shared.Application.Exceptions.ForbiddenException)
            .Should().BeTrue();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var persisted = await db.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == user.Id);
        persisted.Status.Should().Be(UserStatus.LOCKED);
        persisted.LockedFromStatus.Should().Be(UserStatus.ACTIVE);
        (await db.RefreshTokens.CountAsync(token => token.UserId == user.Id && token.RevokedAt == null))
            .Should().Be(0);
        (await CountLockAuditsAsync(db, adminId, user.Id)).Should().Be(1);
    }

    private static async Task AssertLockVsFailedLoginAsync(
        RaceAdminUsersFactory factory,
        Guid adminId,
        int seed)
    {
        var user = await SeedActivePassengerAsync(factory, seed);
        var (lockResult, loginResult) = await RunTogetherAsync(
            factory,
            new LockUserCommand(adminId, UserRole.SYSTEM_ADMIN.ToString(), user.Id, null, null),
            new LoginCommand(user.Email, "WrongPassword123"));

        lockResult.Exception.Should().BeNull();
        loginResult.Exception.Should().BeAssignableTo<Exception>();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var persisted = await db.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == user.Id);
        persisted.Status.Should().Be(UserStatus.LOCKED);
        persisted.LockedFromStatus.Should().Be(UserStatus.ACTIVE);
        persisted.FailedLoginAttempts.Should().BeOneOf(0, 1);
        factory.Counter.Get(user.Id).Should().Be(persisted.FailedLoginAttempts);
        (await CountLockAuditsAsync(db, adminId, user.Id)).Should().Be(1);
    }

    private static async Task AssertUnlockVsFailedLoginAsync(
        RaceAdminUsersFactory factory,
        Guid adminId,
        int seed)
    {
        var user = await SeedActivePassengerAsync(factory, seed, locked: true);
        factory.Counter.Seed(user.Id, 5);
        var (unlockResult, loginResult) = await RunTogetherAsync(
            factory,
            new UnlockUserCommand(adminId, UserRole.SYSTEM_ADMIN.ToString(), user.Id, null, null),
            new LoginCommand(user.Email, "WrongPassword123"));

        unlockResult.Exception.Should().BeNull();
        loginResult.Exception.Should().BeAssignableTo<Exception>();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var persisted = await db.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == user.Id);
        persisted.Status.Should().Be(UserStatus.ACTIVE);
        persisted.LockedFromStatus.Should().BeNull();
        persisted.FailedLoginAttempts.Should().BeOneOf(0, 1);
        factory.Counter.Get(user.Id).Should().Be(persisted.FailedLoginAttempts);
    }

    private static async Task AssertLockVsForgotPasswordAsync(
        RaceAdminUsersFactory factory,
        Guid adminId,
        int seed)
    {
        var user = await SeedActivePassengerAsync(factory, seed);
        var (lockResult, forgotResult) = await RunTogetherAsync(
            factory,
            new LockUserCommand(adminId, UserRole.SYSTEM_ADMIN.ToString(), user.Id, null, null),
            new ForgotPasswordCommand(user.Email));

        lockResult.Exception.Should().BeNull();
        forgotResult.Exception.Should().BeNull();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var persisted = await db.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == user.Id);
        persisted.Status.Should().Be(UserStatus.LOCKED);
        var otpCount = await db.EmailVerificationTokens.CountAsync(token =>
            token.UserId == user.Id && token.Purpose == EmailVerificationPurpose.PASSWORD_RESET);
        otpCount.Should().BeOneOf(0, 1);
        var otpOutboxPayloads = await db.OutboxEvents.AsNoTracking()
            .Where(message => message.EventType == "identity.otp.requested")
            .Select(message => message.Payload)
            .ToListAsync();
        var outboxCount = otpOutboxPayloads.Count(payload =>
            payload.Contains(user.Id.ToString(), StringComparison.Ordinal));
        outboxCount.Should().Be(otpCount);
    }

    private static async Task AssertLockVsResetPasswordAsync(
        RaceAdminUsersFactory factory,
        Guid adminId,
        int seed)
    {
        var code = (seed % 1_000_000).ToString("D6");
        var user = await SeedActivePassengerAsync(factory, seed, resetCode: code);
        var (lockResult, resetResult) = await RunTogetherAsync(
            factory,
            new LockUserCommand(adminId, UserRole.SYSTEM_ADMIN.ToString(), user.Id, null, null),
            new ResetPasswordCommand(user.Email, code, NewPassword));

        lockResult.Exception.Should().BeNull();
        (resetResult.Exception is null
            || resetResult.Exception is VietRide.Shared.Application.Exceptions.BadRequestException)
            .Should().BeTrue();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var persisted = await db.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == user.Id);
        var token = await db.EmailVerificationTokens.AsNoTracking().SingleAsync(candidate =>
            candidate.UserId == user.Id && candidate.Purpose == EmailVerificationPurpose.PASSWORD_RESET);
        persisted.Status.Should().Be(UserStatus.LOCKED);
        persisted.LockedFromStatus.Should().Be(UserStatus.ACTIVE);

        if (resetResult.Exception is null)
        {
            token.UsedAt.Should().NotBeNull();
            hasher.Verify(NewPassword, persisted.PasswordHash!).Should().BeTrue();
        }
        else
        {
            token.UsedAt.Should().BeNull();
            hasher.Verify(OldPassword, persisted.PasswordHash!).Should().BeTrue();
        }

        (await db.RefreshTokens.CountAsync(refresh => refresh.UserId == user.Id && refresh.RevokedAt == null))
            .Should().Be(0);
    }

    private static async Task<(DispatchResult<TFirst> First, DispatchResult<TSecond> Second)> RunTogetherAsync<TFirst, TSecond>(
        RaceAdminUsersFactory factory,
        IRequest<TFirst> first,
        IRequest<TSecond> second)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = DispatchAsync(factory, gate.Task, first);
        var secondTask = DispatchAsync(factory, gate.Task, second);
        gate.SetResult();
        return (await firstTask, await secondTask);
    }

    private static async Task<DispatchResult<T>> DispatchAsync<T>(
        RaceAdminUsersFactory factory,
        Task gate,
        IRequest<T> request)
    {
        await gate;
        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            return new DispatchResult<T>(await sender.Send(request), null);
        }
        catch (Exception exception)
        {
            return new DispatchResult<T>(default, exception);
        }
    }

    private static async Task<Guid> SeedAdminAsync(RaceAdminUsersFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var admin = User.CreateAdminPendingPassword("day40-admin@race.test", "Day 40 Admin");
        admin.SetInitialPassword(hasher.Hash(OldPassword));
        await db.Users.AddAsync(admin);
        await db.SaveChangesAsync();
        return admin.Id;
    }

    private static async Task<User> SeedActivePassengerAsync(
        RaceAdminUsersFactory factory,
        int seed,
        bool locked = false,
        string? resetCode = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var user = User.CreatePassenger(
            $"day40-race-{seed}@example.test",
            VietRide.Shared.Kernel.ValueObjects.PhoneNumber.Parse($"+849{seed:D8}"),
            hasher.Hash(OldPassword),
            $"Race User {seed}");
        user.VerifyEmail();
        if (locked)
            user.Lock();

        await db.Users.AddAsync(user);
        if (resetCode is not null)
        {
            await db.EmailVerificationTokens.AddAsync(EmailVerificationToken.Create(
                user.Id,
                EmailVerificationPurpose.PASSWORD_RESET,
                resetCode,
                DateTimeOffset.UtcNow.AddMinutes(5)));
            var refreshFactory = scope.ServiceProvider.GetRequiredService<IRefreshTokenFactory>();
            var (_, refreshToken) = refreshFactory.Create(user.Id, null, null);
            await db.RefreshTokens.AddAsync(refreshToken);
        }

        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<int> CountLockAuditsAsync(IdentityDbContext db, Guid adminId, Guid targetId)
    {
        var logs = await db.ActivityLogs.AsNoTracking()
            .Where(log => log.UserId == adminId && log.Action == ActivityLogAction.LOCK_USER)
            .Select(log => log.Metadata)
            .ToListAsync();
        return logs.Count(metadata => metadata?.Contains(targetId.ToString(), StringComparison.Ordinal) == true);
    }

    private sealed record DispatchResult<T>(T? Value, Exception? Exception);

    private sealed class RaceAdminUsersFactory : AdminUsersEndpointsTests.DbBackedAdminUsersFactory
    {
        public InMemoryLoginLockoutCounter Counter { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("IdentityJwt:Kid", "day40-race-key");
            builder.UseSetting("IdentityJwt:PrivateKey", AuthWebApplicationFactory.DevPrivateKeyPem);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILoginLockoutCounter>();
                services.RemoveAll<IPasswordResetRateLimiter>();
                services.AddSingleton<ILoginLockoutCounter>(Counter);
                services.AddSingleton<IPasswordResetRateLimiter, AlwaysAllowPasswordResetRateLimiter>();
            });
        }
    }

    public sealed class InMemoryLoginLockoutCounter : ILoginLockoutCounter
    {
        private readonly ConcurrentDictionary<Guid, long> _values = new();

        public Task<long> IncrementAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(_values.AddOrUpdate(userId, 1, (_, value) => value + 1));

        public Task ResetAsync(Guid userId, CancellationToken ct = default)
        {
            _values.TryRemove(userId, out _);
            return Task.CompletedTask;
        }

        public long Get(Guid userId) => _values.TryGetValue(userId, out var value) ? value : 0;

        public void Seed(Guid userId, long value) => _values[userId] = value;
    }

    private sealed class AlwaysAllowPasswordResetRateLimiter : IPasswordResetRateLimiter
    {
        public Task<bool> TryIncrementAsync(string email, CancellationToken ct = default)
            => Task.FromResult(true);
    }
}
