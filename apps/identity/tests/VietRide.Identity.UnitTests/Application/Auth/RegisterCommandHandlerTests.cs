using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Auth.Register;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using Xunit;

namespace VietRide.Identity.UnitTests.Application.Auth;

public sealed class RegisterCommandHandlerTests
{
    private static readonly DateTimeOffset FrozenNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Creates a handler with all dependencies mocked to happy-path defaults.
    /// Individual tests override specific subs before passing them in.
    /// </summary>
    private static RegisterCommandHandler BuildHandler(
        IUserRepository users,
        IEmailVerificationTokenRepository tokens,
        IPasswordHasher hasher,
        IEmailService email,
        IOtpRateLimiter rateLimiter,
        IClock clock)
    {
        return new RegisterCommandHandler(users, tokens, hasher, email, rateLimiter, clock,
            Substitute.For<ILogger<RegisterCommandHandler>>());
    }

    private static (
        IUserRepository users,
        IEmailVerificationTokenRepository tokens,
        IPasswordHasher hasher,
        IEmailService email,
        IOtpRateLimiter rateLimiter,
        IClock clock) MakeDefaults()
    {
        var users = Substitute.For<IUserRepository>();
        var tokens = Substitute.For<IEmailVerificationTokenRepository>();
        var hasher = Substitute.For<IPasswordHasher>();
        var email = Substitute.For<IEmailService>();
        var rateLimiter = Substitute.For<IOtpRateLimiter>();
        var clock = Substitute.For<IClock>();

        clock.UtcNow.Returns(FrozenNow);
        hasher.Hash(Arg.Any<string>()).Returns("$2a$12$fakehash");
        rateLimiter.TryIncrementAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        users.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        users.GetByPhoneAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        users.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<User>());
        tokens.TryAddAsync(Arg.Any<EmailVerificationToken>(), Arg.Any<CancellationToken>())
            .Returns(true);

        return (users, tokens, hasher, email, rateLimiter, clock);
    }

    // -------------------------------------------------------------------------
    // Happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_HappyPath_Returns201Response()
    {
        var (users, tokens, hasher, email, rateLimiter, clock) = MakeDefaults();
        var handler = BuildHandler(users, tokens, hasher, email, rateLimiter, clock);

        var result = await handler.Handle(
            new RegisterCommand("test@example.com", "password123", "Test User", "0901234567"),
            CancellationToken.None);

        result.Should().NotBeNull();
        result.Email.Should().Be("test@example.com");
        result.Status.Should().Be(UserStatus.PENDING_EMAIL_VERIFICATION.ToString());
        result.OtpTtlMinutes.Should().Be(5);
    }

    [Fact]
    public async Task Handle_NormalizesLocalPhone_To_E164()
    {
        var (users, tokens, hasher, email, rateLimiter, clock) = MakeDefaults();

        User? capturedUser = null;
        users.AddAsync(Arg.Do<User>(u => capturedUser = u), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<User>());

        var handler = BuildHandler(users, tokens, hasher, email, rateLimiter, clock);

        await handler.Handle(
            new RegisterCommand("t@e.com", "pass1234", "Name", "0901234567"),
            CancellationToken.None);

        capturedUser.Should().NotBeNull();
        capturedUser!.Phone!.Value.Value.Should().Be("+84901234567");
    }

    // -------------------------------------------------------------------------
    // Error cases
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_DuplicateEmail_Throws409()
    {
        var (users, tokens, hasher, email, rateLimiter, clock) = MakeDefaults();

        var existingUser = User.CreatePassenger(
            "dup@example.com",
            PhoneNumber.Parse("+84901234567"),
            "hash",
            "Dup");
        users.GetByEmailAsync("dup@example.com", Arg.Any<CancellationToken>()).Returns(existingUser);

        var handler = BuildHandler(users, tokens, hasher, email, rateLimiter, clock);

        var act = () => handler.Handle(
            new RegisterCommand("dup@example.com", "pass1234", "Name", "0901234567"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.ErrorCode == "AUTH_EMAIL_ALREADY_REGISTERED");
    }

    [Fact]
    public async Task Handle_DuplicatePhone_Throws409()
    {
        var (users, tokens, hasher, email, rateLimiter, clock) = MakeDefaults();

        var existingUser = User.CreatePassenger(
            "other@example.com",
            PhoneNumber.Parse("+84901234567"),
            "hash",
            "Other");
        users.GetByPhoneAsync("+84901234567", Arg.Any<CancellationToken>()).Returns(existingUser);

        var handler = BuildHandler(users, tokens, hasher, email, rateLimiter, clock);

        var act = () => handler.Handle(
            new RegisterCommand("new@example.com", "pass1234", "Name", "0901234567"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.ErrorCode == "AUTH_PHONE_ALREADY_REGISTERED");
    }

    [Fact]
    public async Task Handle_InvalidPhone_Throws400WithAuthPhoneInvalidFormat()
    {
        // Invoke the handler directly with a phone that PhoneNumber.Normalize() cannot parse.
        // FluentValidation is bypassed here (direct handler call), exercising the handler-level
        // BadRequestException("AUTH_PHONE_INVALID_FORMAT", ...) guard in step 1.
        var (users, tokens, hasher, email, rateLimiter, clock) = MakeDefaults();
        var handler = BuildHandler(users, tokens, hasher, email, rateLimiter, clock);

        var act = () => handler.Handle(
            new RegisterCommand("x@x.com", "pass1234", "Name", "not-a-phone"),
            CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>()
            .Where(e => e.ErrorCode == "AUTH_PHONE_INVALID_FORMAT");
    }

    [Fact]
    public async Task Handle_RateLimitExceeded_Throws429()
    {
        var (users, tokens, hasher, email, rateLimiter, clock) = MakeDefaults();

        // Override: rate limit exceeded.
        rateLimiter.TryIncrementAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var handler = BuildHandler(users, tokens, hasher, email, rateLimiter, clock);

        var act = () => handler.Handle(
            new RegisterCommand("r@r.com", "pass1234", "Name", "0901234567"),
            CancellationToken.None);

        // Rate limit → 429, not 409 (TooManyRequestsException, BSOT §5.9 + Task 3.5).
        await act.Should().ThrowAsync<TooManyRequestsException>()
            .Where(e => e.ErrorCode == "AUTH_OTP_RATE_LIMIT_EXCEEDED");
    }
}
