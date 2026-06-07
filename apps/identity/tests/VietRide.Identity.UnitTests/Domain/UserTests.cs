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
    // CreateGoogleAccount
    // -------------------------------------------------------------------------

    [Fact]
    public void CreateGoogleAccount_Sets_ActivePassenger_WithNoPhoneOrPassword()
    {
        var user = User.CreateGoogleAccount(
            "  Google.User@Example.COM  ",
            "Google User",
            "https://example.com/avatar.png");

        user.Email.Should().Be("google.user@example.com");
        user.DisplayName.Should().Be("Google User");
        user.AvatarUrl.Should().Be("https://example.com/avatar.png");
        user.Role.Should().Be(UserRole.PASSENGER);
        user.Status.Should().Be(UserStatus.ACTIVE);
        user.Phone.Should().BeNull();
        user.PasswordHash.Should().BeNull();
        user.FailedLoginAttempts.Should().Be(0);
    }

    // -------------------------------------------------------------------------
    // CreateAdminPendingPassword
    // -------------------------------------------------------------------------

    [Fact]
    public void CreateAdminPendingPassword_Sets_SystemAdminPendingInitialPassword_WithNoPasswordOrOperator()
    {
        var user = User.CreateAdminPendingPassword(
            "  Admin@Example.COM  ",
            "System Admin");

        user.Email.Should().Be("admin@example.com");
        user.DisplayName.Should().Be("System Admin");
        user.Role.Should().Be(UserRole.SYSTEM_ADMIN);
        user.Status.Should().Be(UserStatus.PENDING_INITIAL_PASSWORD);
        user.Phone.Should().BeNull();
        user.PasswordHash.Should().BeNull();
        user.OperatorId.Should().BeNull();
        user.FailedLoginAttempts.Should().Be(0);
    }

    // -------------------------------------------------------------------------
    // Operator admin factories
    // -------------------------------------------------------------------------

    [Fact]
    public void CreateOperatorAdminPendingEmailVerification_SetsOperatorAdminPendingEmail_WithPasswordAndOperator()
    {
        var operatorId = Guid.NewGuid();
        var user = User.CreateOperatorAdminPendingEmailVerification(
            "  Operator.Admin@Example.COM  ",
            TestPhone,
            "$2a$12$hashedpassword",
            "Operator Admin",
            operatorId);

        user.Email.Should().Be("operator.admin@example.com");
        user.DisplayName.Should().Be("Operator Admin");
        user.Role.Should().Be(UserRole.OPERATOR_ADMIN);
        user.Status.Should().Be(UserStatus.PENDING_EMAIL_VERIFICATION);
        user.Phone.Should().Be(TestPhone);
        user.PasswordHash.Should().Be("$2a$12$hashedpassword");
        user.OperatorId.Should().Be(operatorId);
    }

    [Fact]
    public void CreateOperatorAdminPendingPassword_SetsOperatorAdminPendingInitialPassword_WithNoPassword()
    {
        var operatorId = Guid.NewGuid();
        var user = User.CreateOperatorAdminPendingPassword(
            "  Operator.Admin@Example.COM  ",
            TestPhone,
            "Operator Admin",
            operatorId);

        user.Email.Should().Be("operator.admin@example.com");
        user.DisplayName.Should().Be("Operator Admin");
        user.Role.Should().Be(UserRole.OPERATOR_ADMIN);
        user.Status.Should().Be(UserStatus.PENDING_INITIAL_PASSWORD);
        user.Phone.Should().Be(TestPhone);
        user.PasswordHash.Should().BeNull();
        user.OperatorId.Should().Be(operatorId);
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
    // SetInitialPassword
    // -------------------------------------------------------------------------

    [Fact]
    public void SetInitialPassword_WhenPendingInitialPassword_SetsHashAndTransitionsToActive()
    {
        var user = User.CreateAdminPendingPassword("admin@example.com", "System Admin");

        user.SetInitialPassword("$2a$12$hashed-initial-password");

        user.PasswordHash.Should().Be("$2a$12$hashed-initial-password");
        user.Status.Should().Be(UserStatus.ACTIVE);
    }

    [Fact]
    public void SetInitialPassword_WhenNotPendingInitialPassword_ThrowsInvalidUserStatusTransition()
    {
        var user = MakeActivePassenger();

        var act = () => user.SetInitialPassword("$2a$12$hashed-initial-password");

        act.Should().Throw<InvalidUserStatusTransitionException>();
    }

    [Fact]
    public void SetInitialPassword_WhenHashBlank_ThrowsArgumentException()
    {
        var user = User.CreateAdminPendingPassword("admin@example.com", "System Admin");

        var act = () => user.SetInitialPassword(" ");

        act.Should().Throw<ArgumentException>();
        user.Status.Should().Be(UserStatus.PENDING_INITIAL_PASSWORD);
        user.PasswordHash.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // CompleteProfile
    // -------------------------------------------------------------------------

    [Fact]
    public void CompleteProfile_WhenPhoneIsNull_SetsPhone()
    {
        var user = User.CreateGoogleAccount(
            "google.user@example.com",
            "Google User",
            null);
        var phone = PhoneNumber.Parse("+84987654321");

        user.CompleteProfile(phone);

        user.Phone.Should().Be(phone);
    }

    [Fact]
    public void CompleteProfile_WhenPhoneAlreadySet_ThrowsValidationDomainException()
    {
        var user = MakeActivePassenger();
        var phone = PhoneNumber.Parse("+84987654321");

        var act = () => user.CompleteProfile(phone);

        act.Should().Throw<IdentityDomainException>()
            .Which.ErrorCode.Should().Be("VALIDATION_ERROR");
        user.Phone.Should().Be(TestPhone);
    }

    // -------------------------------------------------------------------------
    // RecordFailedLogin — increment + boundary
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordFailedLogin_Increments_FailedLoginAttempts_And_SetsLastFailedAt()
    {
        var clock = FrozenClock();
        var user = MakeActivePassenger();

        user.RecordFailedLogin(clock, 1);

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
            user.RecordFailedLogin(clock, i + 1);
        }


        user.Status.Should().Be(UserStatus.ACTIVE);
        user.FailedLoginAttempts.Should().Be(4);

        // Fifth increment — transitions to LOCKED
        user.RecordFailedLogin(clock, 5);

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
        user.RecordFailedLogin(clock, 1);
        user.RecordFailedLogin(clock, 2);

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
        user.RecordFailedLogin(clock, 1);

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
