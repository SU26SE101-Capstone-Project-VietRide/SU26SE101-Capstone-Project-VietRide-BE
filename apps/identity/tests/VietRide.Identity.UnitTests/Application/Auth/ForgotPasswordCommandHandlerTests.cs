using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Events;
using VietRide.Identity.Application.Features.Auth.ForgotPassword;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using Xunit;

namespace VietRide.Identity.UnitTests.Application.Auth;

public sealed class ForgotPasswordCommandHandlerTests
{
    private static readonly DateTimeOffset FrozenNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly PhoneNumber TestPhone = PhoneNumber.Parse("+84901234567");

    [Theory]
    [InlineData(UserRole.PASSENGER)]
    [InlineData(UserRole.DRIVER)]
    [InlineData(UserRole.ASSISTANT)]
    [InlineData(UserRole.OPERATOR_STAFF)]
    [InlineData(UserRole.OPERATOR_ADMIN)]
    [InlineData(UserRole.SYSTEM_ADMIN)]
    public async Task Handle_ActiveUser_CreatesPasswordResetOtpAndOutboxEvent(UserRole role)
    {
        var user = CreateActiveUser(role);
        var (users, tokens, rateLimiter, clock, outbox) = MakeDefaults(user);
        EmailVerificationToken? capturedToken = null;
        tokens.TryAddAsync(Arg.Do<EmailVerificationToken>(t => capturedToken = t), Arg.Any<CancellationToken>())
            .Returns(true);
        var capturedEvents = new List<(string EventType, string Payload)>();
        outbox.EnqueueAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturedEvents.Add((ci.ArgAt<string>(0), ci.ArgAt<string>(1)));
                return Task.CompletedTask;
            });
        var handler = BuildHandler(users, tokens, rateLimiter, clock, outbox);

        var result = await handler.Handle(new ForgotPasswordCommand($" {user.Email.ToUpperInvariant()} "), CancellationToken.None);

        result.Email.Should().Be(user.Email);
        result.OtpTtlMinutes.Should().Be(5);
        await tokens.Received(1).RevokeActiveByUserAndPurposeAsync(
            user.Id,
            EmailVerificationPurpose.PASSWORD_RESET,
            FrozenNow,
            Arg.Any<CancellationToken>());
        capturedToken.Should().NotBeNull();
        capturedToken!.Purpose.Should().Be(EmailVerificationPurpose.PASSWORD_RESET);
        capturedToken.ExpiresAt.Should().Be(FrozenNow.AddMinutes(5));

        var otpEntry = capturedEvents.Should().ContainSingle(e => e.EventType == OtpRequestedIntegrationEvent.EventType).Which;
        using var doc = JsonDocument.Parse(otpEntry.Payload);
        doc.RootElement.GetProperty("userId").GetGuid().Should().Be(user.Id);
        doc.RootElement.GetProperty("email").GetString().Should().Be(user.Email);
        doc.RootElement.GetProperty("purpose").GetString().Should().Be("PASSWORD_RESET");
        doc.RootElement.GetProperty("ttlMinutes").GetInt32().Should().Be(5);
        doc.RootElement.GetProperty("code").GetString().Should().HaveLength(6);
    }

    [Fact]
    public async Task Handle_UnknownEmail_ReturnsGenericSuccessWithoutOtp()
    {
        var (users, tokens, rateLimiter, clock, outbox) = MakeDefaults(user: null);
        var handler = BuildHandler(users, tokens, rateLimiter, clock, outbox);

        var result = await handler.Handle(new ForgotPasswordCommand("missing@example.com"), CancellationToken.None);

        result.Email.Should().Be("missing@example.com");
        result.OtpTtlMinutes.Should().Be(5);
        await tokens.DidNotReceive().TryAddAsync(Arg.Any<EmailVerificationToken>(), Arg.Any<CancellationToken>());
        await outbox.DidNotReceive().EnqueueAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NonActiveUser_ReturnsGenericSuccessWithoutOtp()
    {
        var user = User.CreatePassenger("pending@example.com", TestPhone, "hash", "Pending");
        var (users, tokens, rateLimiter, clock, outbox) = MakeDefaults(user);
        var handler = BuildHandler(users, tokens, rateLimiter, clock, outbox);

        var result = await handler.Handle(new ForgotPasswordCommand(user.Email), CancellationToken.None);

        result.Email.Should().Be(user.Email);
        result.OtpTtlMinutes.Should().Be(5);
        await tokens.DidNotReceive().TryAddAsync(Arg.Any<EmailVerificationToken>(), Arg.Any<CancellationToken>());
        await outbox.DidNotReceive().EnqueueAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RateLimitExceeded_ThrowsAuthOtpRateLimitExceeded()
    {
        var (users, tokens, rateLimiter, clock, outbox) = MakeDefaults(CreateActiveUser(UserRole.PASSENGER));
        rateLimiter.TryIncrementAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var handler = BuildHandler(users, tokens, rateLimiter, clock, outbox);

        var act = () => handler.Handle(new ForgotPasswordCommand("user@example.com"), CancellationToken.None);

        await act.Should().ThrowAsync<TooManyRequestsException>()
            .Where(e => e.ErrorCode == "AUTH_OTP_RATE_LIMIT_EXCEEDED");
        await tokens.DidNotReceive().TryAddAsync(Arg.Any<EmailVerificationToken>(), Arg.Any<CancellationToken>());
        await outbox.DidNotReceive().EnqueueAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static ForgotPasswordCommandHandler BuildHandler(
        IUserRepository users,
        IEmailVerificationTokenRepository tokens,
        IPasswordResetRateLimiter rateLimiter,
        IClock clock,
        IIntegrationEventOutbox outbox)
        => new(
            users,
            tokens,
            rateLimiter,
            clock,
            outbox,
            Substitute.For<ILogger<ForgotPasswordCommandHandler>>());

    private static (
        IUserRepository users,
        IEmailVerificationTokenRepository tokens,
        IPasswordResetRateLimiter rateLimiter,
        IClock clock,
        IIntegrationEventOutbox outbox) MakeDefaults(User? user)
    {
        var users = Substitute.For<IUserRepository>();
        var tokens = Substitute.For<IEmailVerificationTokenRepository>();
        var rateLimiter = Substitute.For<IPasswordResetRateLimiter>();
        var clock = Substitute.For<IClock>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();

        clock.UtcNow.Returns(FrozenNow);
        users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
        rateLimiter.TryIncrementAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        tokens.TryAddAsync(Arg.Any<EmailVerificationToken>(), Arg.Any<CancellationToken>()).Returns(true);

        return (users, tokens, rateLimiter, clock, outbox);
    }

    private static User CreateActiveUser(UserRole role)
    {
        var user = role switch
        {
            UserRole.PASSENGER => User.CreatePassenger("passenger@example.com", TestPhone, "old-hash", "Passenger"),
            UserRole.SYSTEM_ADMIN => User.CreateAdminPendingPassword("system-admin@example.com", "System Admin"),
            UserRole.OPERATOR_ADMIN => User.CreateOperatorAdminPendingPassword("operator-admin@example.com", TestPhone, "Operator Admin", Guid.NewGuid()),
            _ => User.CreateOperatorScopedPendingPassword($"{role.ToString().ToLowerInvariant()}@example.com", TestPhone, "Operator User", role, Guid.NewGuid()),
        };

        if (user.Status == UserStatus.PENDING_EMAIL_VERIFICATION)
        {
            user.VerifyEmail();
        }
        else if (user.Status == UserStatus.PENDING_INITIAL_PASSWORD)
        {
            user.SetInitialPassword("old-hash");
        }

        return user;
    }
}
