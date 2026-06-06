using MediatR;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Auth.SetInitialPassword;

public sealed class SetInitialPasswordCommandHandler : IRequestHandler<SetInitialPasswordCommand, SetInitialPasswordResponseDto>
{
    private readonly IUserRepository _users;
    private readonly IEmailVerificationTokenRepository _tokens;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IClock _clock;

    public SetInitialPasswordCommandHandler(
        IUserRepository users,
        IEmailVerificationTokenRepository tokens,
        IPasswordHasher passwordHasher,
        IClock clock)
    {
        _users = users;
        _tokens = tokens;
        _passwordHasher = passwordHasher;
        _clock = clock;
    }

    public async Task<SetInitialPasswordResponseDto> Handle(
        SetInitialPasswordCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            throw InvalidToken();

        var now = _clock.UtcNow;
        var token = await _tokens.FindByCodeAndPurposeAsync(
            request.Token.Trim(),
            EmailVerificationPurpose.SET_INITIAL_PASSWORD,
            cancellationToken);

        if (token is null)
            throw InvalidToken();

        if (token.ExpiresAt <= now)
            throw new BadRequestException(
                "AUTH_INITIAL_PASSWORD_TOKEN_EXPIRED",
                "Initial password token has expired.");

        var user = await _users.GetByIdAsync(token.UserId, cancellationToken)
            ?? throw InvalidToken();

        var passwordHash = _passwordHasher.Hash(request.Password);
        user.SetInitialPassword(passwordHash);
        token.MarkUsed(now);
        _tokens.Update(token);

        return new SetInitialPasswordResponseDto(
            UserId: user.Id,
            Status: user.Status.ToString());
    }

    private static BadRequestException InvalidToken()
        => new(
            "AUTH_INITIAL_PASSWORD_TOKEN_INVALID",
            "Initial password token is invalid.");
}
