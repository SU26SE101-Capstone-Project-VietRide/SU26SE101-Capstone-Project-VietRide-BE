using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Auth.SetInitialPassword;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.UnitTests.Application.Auth;

public sealed class SetInitialPasswordCommandHandlerTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IEmailVerificationTokenRepository _tokens = Substitute.For<IEmailVerificationTokenRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public SetInitialPasswordCommandHandlerTests()
    {
        _clock.UtcNow.Returns(FixedNow);
        _passwordHasher.Hash("StrongPassword123").Returns("$2a$12$hashed-initial-password");
    }

    [Theory]
    [InlineData("OnlyLetters")]
    [InlineData("12345678")]
    public void Validate_WhenPasswordMissingLetterOrDigit_Fails(string password)
    {
        var validator = new SetInitialPasswordCommandValidator();

        var result = validator.Validate(new SetInitialPasswordCommand("initial-token", password));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(SetInitialPasswordCommand.Password));
    }

    [Fact]
    public async Task Handle_HappyPath_HashesPassword_MarksTokenUsed_ActivatesUser()
    {
        var user = User.CreateAdminPendingPassword("admin@example.com", "System Admin");
        var token = EmailVerificationToken.Create(
            user.Id,
            EmailVerificationPurpose.SET_INITIAL_PASSWORD,
            "initial-token",
            FixedNow.AddHours(1));
        _tokens.FindByCodeAndPurposeAsync(
                "initial-token",
                EmailVerificationPurpose.SET_INITIAL_PASSWORD,
                Arg.Any<CancellationToken>())
            .Returns(token);
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var handler = CreateHandler();

        var result = await handler.Handle(
            new SetInitialPasswordCommand("initial-token", "StrongPassword123"),
            CancellationToken.None);

        result.UserId.Should().Be(user.Id);
        result.Status.Should().Be("ACTIVE");
        user.Status.Should().Be(UserStatus.ACTIVE);
        user.PasswordHash.Should().Be("$2a$12$hashed-initial-password");
        token.UsedAt.Should().Be(FixedNow);
        _tokens.Received(1).Update(token);
    }

    [Fact]
    public async Task Handle_WhenTokenMissing_ThrowsInvalidTokenBadRequest()
    {
        _tokens.FindByCodeAndPurposeAsync(
                "missing-token",
                EmailVerificationPurpose.SET_INITIAL_PASSWORD,
                Arg.Any<CancellationToken>())
            .Returns((EmailVerificationToken?)null);
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new SetInitialPasswordCommand("missing-token", "StrongPassword123"),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<BadRequestException>();
        ex.Which.ErrorCode.Should().Be("AUTH_INITIAL_PASSWORD_TOKEN_INVALID");
    }

    [Fact]
    public async Task Handle_WhenTokenMissing_ThrowsInvalidTokenBadRequestWithoutLookup()
    {
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new SetInitialPasswordCommand(null, "StrongPassword123"),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<BadRequestException>();
        ex.Which.ErrorCode.Should().Be("AUTH_INITIAL_PASSWORD_TOKEN_INVALID");
        await _tokens.DidNotReceive().FindByCodeAndPurposeAsync(
            Arg.Any<string>(),
            Arg.Any<EmailVerificationPurpose>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenTokenExpired_ThrowsExpiredTokenBadRequest()
    {
        var user = User.CreateAdminPendingPassword("admin@example.com", "System Admin");
        var token = EmailVerificationToken.Create(
            user.Id,
            EmailVerificationPurpose.SET_INITIAL_PASSWORD,
            "expired-token",
            FixedNow.AddSeconds(-1));
        _tokens.FindByCodeAndPurposeAsync(
                "expired-token",
                EmailVerificationPurpose.SET_INITIAL_PASSWORD,
                Arg.Any<CancellationToken>())
            .Returns(token);
        var handler = CreateHandler();

        var act = () => handler.Handle(
            new SetInitialPasswordCommand("expired-token", "StrongPassword123"),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<BadRequestException>();
        ex.Which.ErrorCode.Should().Be("AUTH_INITIAL_PASSWORD_TOKEN_EXPIRED");
        token.UsedAt.Should().BeNull();
    }

    private SetInitialPasswordCommandHandler CreateHandler()
        => new(_users, _tokens, _passwordHasher, _clock);
}
