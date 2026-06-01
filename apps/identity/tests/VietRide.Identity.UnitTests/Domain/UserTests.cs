using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Domain.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.UnitTests.Domain;

public sealed class UserTests
{
    private static readonly PhoneNumber TestPhone = PhoneNumber.Parse("+84901234567");
    private static readonly DateTimeOffset FixedNow =
        new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static IClock FrozenClock()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        return clock;
    }

    private static User MakeActivePassenger()
    {
        var user = User.CreatePassenger(
            "test@example.com",
            TestPhone,
            "$2a$12$hashedpassword",
            "Test User");
        user.VerifyEmail();
        return user;
    }

    // -------------------------------------------------------------------------
    // CreatePassenger
    // -------------------------------------------------------------------------

    [Fact]
    public void CreatePassenger_Sets_PendingEmailVerification_Status()
    {
        var user = User.CreatePassenger(
            "test@example.com",
            TestPhone,
            "$2a$12$hashedpassword",
            "Test User");

        user.Status.Should().Be(UserStatus.PENDING_EMAIL_VERIFICATION);
        user.Role.Should().Be(UserRole.PASSENGER);
        user.FailedLoginAttempts.Should().Be(0);
        user.DeletedAt.Should().BeNull("users table has no is_active column; deleted_at IS NULL means the record is live");
    }

    [Fact]
    public void CreatePassenger_NormalizesEmail_ToLowerInvariant()
    {
        var user = User.CreatePassenger(
            "  Test@Example.COM  ",
            TestPhone,
            "$2a$12$hashedpassword",
            "Test User");

        user.Email.Should().Be("test@example.com");
    }

    // -------------------------------------------------------------------------
    // VerifyEmail
    // -------------------------------------------------------------------------

    [Fact]
    public void VerifyEmail_HappyPath_TransitionsToActive()
    {
        var user = User.CreatePassenger(
            "test@example.com",
            TestPhone,
            "$2a$12$hashedpassword",
            "Test User");

        user.VerifyEmail();

        user.Status.Should().Be(UserStatus.ACTIVE);
    }

    [Fact]
    public void VerifyEmail_WhenAlreadyActive_ThrowsInvalidUserStatusTransition()
    {
        var user = MakeActivePassenger();

        var act = () => user.VerifyEmail();

        act.Should().Throw<InvalidUserStatusTransitionException>();
    }

    // -------------------------------------------------------------------------
    // RecordFailedLogin — increment + boundary
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordFailedLogin_Increments_FailedLoginAttempts_And_SetsLastFailedAt()
    {
        var clock = FrozenClock();
        var user = MakeActivePassenger();

        user.RecordFailedLogin(clock);

        user.FailedLoginAttempts.Should().Be(1);
        user.LastFailedLoginAt.Should().Be(FixedNow);
        user.Status.Should().Be(UserStatus.ACTIVE);
    }

    [Fact]
    public void RecordFailedLogin_AtFiveAttempts_LocksAccount_WithNoLockedUntilField()
    {
        var clock = FrozenClock();
        var user = MakeActivePassenger();

        // Four increments — still ACTIVE
        for (var i = 0; i < 4; i++)
        {
            user.RecordFailedLogin(clock);
        }

        user.Status.Should().Be(UserStatus.ACTIVE);
        user.FailedLoginAttempts.Should().Be(4);

        // Fifth increment — transitions to LOCKED
        user.RecordFailedLogin(clock);

        user.Status.Should().Be(UserStatus.LOCKED);
        user.FailedLoginAttempts.Should().Be(5);
        // Verify there is no LockedUntil property on the entity (schema has no such column).
        typeof(User).GetProperty("LockedUntil").Should().BeNull(
            "schema.sql has no locked_until column — the lock is permanent until admin unlock");
    }

    // -------------------------------------------------------------------------
    // ResetFailedLogins
    // -------------------------------------------------------------------------

    [Fact]
    public void ResetFailedLogins_Zeros_Counter_And_ClearsLastFailedAt()
    {
        var clock = FrozenClock();
        var user = MakeActivePassenger();
        user.RecordFailedLogin(clock);
        user.RecordFailedLogin(clock);

        user.ResetFailedLogins();

        user.FailedLoginAttempts.Should().Be(0);
        user.LastFailedLoginAt.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // RecordSuccessfulLogin
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordSuccessfulLogin_Sets_LastLoginAt_And_ResetCounter()
    {
        var clock = FrozenClock();
        var user = MakeActivePassenger();
        user.RecordFailedLogin(clock);

        user.RecordSuccessfulLogin(clock);

        user.LastLoginAt.Should().Be(FixedNow);
        user.FailedLoginAttempts.Should().Be(0);
        user.LastFailedLoginAt.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // Lock
    // -------------------------------------------------------------------------

    [Fact]
    public void Lock_Transitions_Status_To_Locked()
    {
        var user = MakeActivePassenger();

        user.Lock();

        user.Status.Should().Be(UserStatus.LOCKED);
    }
}
