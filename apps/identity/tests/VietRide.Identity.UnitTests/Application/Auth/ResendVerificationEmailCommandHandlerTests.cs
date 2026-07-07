using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Events;
using VietRide.Identity.Application.Features.Auth.ResendVerificationEmail;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using Xunit;

namespace VietRide.Identity.UnitTests.Application.Auth;

public sealed class ResendVerificationEmailCommandHandlerTests
{
    private static readonly DateTimeOffset FrozenNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly PhoneNumber TestPhone = PhoneNumber.Parse("+84901234567");

    private static ResendVerificationEmailCommandHandler BuildHandler(
        IUserRepository users,
        IEmailVerificationTokenRepository tokens,
        IOtpRateLimiter rateLimiter,
        IClock clock,
        IIntegrationEventOutbox outbox)
        => new(
            users,
            tokens,
            rateLimiter,
            clock,
            outbox,
            Substitute.For<ILogger<ResendVerificationEmailCommandHandler>>());

    private static (
        IUserRepository users,
        IEmailVerificationTokenRepository tokens,
        IOtpRateLimiter rateLimiter,
        IClock clock,
        IIntegrationEventOutbox outbox) MakeDefaults(User? user = null)
    {
        var users = Substitute.For<IUserRepository>();
        var tokens = Substitute.For<IEmailVerificationTokenRepository>();
        var rateLimiter = Substitute.For<IOtpRateLimiter>();
        var clock = Substitute.For<IClock>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();

        user ??= MakePendingUser();
        clock.UtcNow.Returns(FrozenNow);
        users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        rateLimiter.TryIncrementAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        tokens.TryAddAsync(Arg.Any<EmailVerificationToken>(), Arg.Any<CancellationToken>()).Returns(true);

        return (users, tokens, rateLimiter, clock, outbox);
    }

    private static User MakePendingUser(string email = "user@example.com")
        => User.CreatePassenger(email, TestPhone, "hash", "Test User");

    [Fact]
    public async Task Handle_PendingUser_RevokesOldOtpCreatesNewOtpAndEnqueuesOutboxEvent()
    {
        var user = MakePendingUser();
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

        var result = await handler.Handle(
            new ResendVerificationEmailCommand(" USER@example.com ", "REGISTRATION"),
            CancellationToken.None);

        result.Email.Should().Be("user@example.com");
        result.Status.Should().Be(UserStatus.PENDING_EMAIL_VERIFICATION.ToString());
        result.OtpTtlMinutes.Should().Be(5);

        await tokens.Received(1).RevokeActiveByUserAndPurposeAsync(
            user.Id,
            EmailVerificationPurpose.REGISTRATION,
            FrozenNow,
            Arg.Any<CancellationToken>());
        capturedToken.Should().NotBeNull();
        capturedToken!.UserId.Should().Be(user.Id);
        capturedToken.Purpose.Should().Be(EmailVerificationPurpose.REGISTRATION);
        capturedToken.ExpiresAt.Should().Be(FrozenNow.AddMinutes(5));

        var otpEntry = capturedEvents.Should().ContainSingle(e => e.EventType == OtpRequestedIntegrationEvent.EventType).Which;
        using var otpDoc = JsonDocument.Parse(otpEntry.Payload);
        var root = otpDoc.RootElement;
        root.GetProperty("userId").GetGuid().Should().Be(user.Id);
        root.GetProperty("email").GetString().Should().Be("user@example.com");
        root.GetProperty("purpose").GetString().Should().Be("REGISTRATION");
        root.GetProperty("ttlMinutes").GetInt32().Should().Be(5);
        root.GetProperty("code").GetString().Should().HaveLength(6);
    }

    [Fact]
    public async Task Handle_ActiveUser_ThrowsAuthEmailAlreadyVerified()
    {
        var user = MakePendingUser();
        user.VerifyEmail();
        var (users, tokens, rateLimiter, clock, outbox) = MakeDefaults(user);
        var handler = BuildHandler(users, tokens, rateLimiter, clock, outbox);

        var act = () => handler.Handle(
            new ResendVerificationEmailCommand(user.Email, "REGISTRATION"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.ErrorCode == "AUTH_EMAIL_ALREADY_VERIFIED");
        await tokens.DidNotReceive().TryAddAsync(Arg.Any<EmailVerificationToken>(), Arg.Any<CancellationToken>());
        await outbox.DidNotReceive().EnqueueAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RateLimitExceeded_ThrowsAuthOtpRateLimitExceeded()
    {
        var (users, tokens, rateLimiter, clock, outbox) = MakeDefaults();
        rateLimiter.TryIncrementAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var handler = BuildHandler(users, tokens, rateLimiter, clock, outbox);

        var act = () => handler.Handle(
            new ResendVerificationEmailCommand("user@example.com", "REGISTRATION"),
            CancellationToken.None);

        await act.Should().ThrowAsync<TooManyRequestsException>()
            .Where(e => e.ErrorCode == "AUTH_OTP_RATE_LIMIT_EXCEEDED");
        await tokens.DidNotReceive().TryAddAsync(Arg.Any<EmailVerificationToken>(), Arg.Any<CancellationToken>());
        await outbox.DidNotReceive().EnqueueAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvalidPurpose_ThrowsAuthOtpInvalidWithoutSideEffects()
    {
        var (users, tokens, rateLimiter, clock, outbox) = MakeDefaults();
        var handler = BuildHandler(users, tokens, rateLimiter, clock, outbox);

        var act = () => handler.Handle(
            new ResendVerificationEmailCommand("user@example.com", "SET_INITIAL_PASSWORD"),
            CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>()
            .Where(e => e.ErrorCode == "AUTH_OTP_INVALID");
        await users.DidNotReceive().GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await tokens.DidNotReceive().TryAddAsync(Arg.Any<EmailVerificationToken>(), Arg.Any<CancellationToken>());
        await outbox.DidNotReceive().EnqueueAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UserNotFound_ThrowsAuthOtpInvalidWithoutSideEffects()
    {
        var (users, tokens, rateLimiter, clock, outbox) = MakeDefaults();
        users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        var handler = BuildHandler(users, tokens, rateLimiter, clock, outbox);

        var act = () => handler.Handle(
            new ResendVerificationEmailCommand("missing@example.com", "REGISTRATION"),
            CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>()
            .Where(e => e.ErrorCode == "AUTH_OTP_INVALID");
        await tokens.DidNotReceive().TryAddAsync(Arg.Any<EmailVerificationToken>(), Arg.Any<CancellationToken>());
        await outbox.DidNotReceive().EnqueueAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
