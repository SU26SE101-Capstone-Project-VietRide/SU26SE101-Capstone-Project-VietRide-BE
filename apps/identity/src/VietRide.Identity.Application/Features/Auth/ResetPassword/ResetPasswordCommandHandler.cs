using MediatR;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Features.Auth.ResetPassword;

public sealed class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, ResetPasswordResponseDto>
{
    private readonly IUserRepository _users;
    private readonly IPasswordResetSessionExecutor _sessionExecutor;
    private readonly IPasswordHasher _passwordHasher;

    public ResetPasswordCommandHandler(
        IUserRepository users,
        IPasswordResetSessionExecutor sessionExecutor,
        IPasswordHasher passwordHasher)
    {
        _users = users;
        _sessionExecutor = sessionExecutor;
        _passwordHasher = passwordHasher;
    }

    public async Task<ResetPasswordResponseDto> Handle(
        ResetPasswordCommand request,
        CancellationToken cancellationToken)
    {
        var emailLower = request.Email.Trim().ToLowerInvariant();
        var user = await _users.GetByEmailAsync(emailLower, cancellationToken)
            ?? throw InvalidOtp();

        var result = await _sessionExecutor.ExecuteAsync(
            user.Id,
            request.Code,
            _passwordHasher.Hash(request.NewPassword),
            cancellationToken);

        if (result.Status == PasswordResetSessionStatus.EXPIRED_OTP)
            throw new BadRequestException("AUTH_OTP_EXPIRED", "Verification code has expired.");

        if (result.Status != PasswordResetSessionStatus.SUCCEEDED)
            throw InvalidOtp();

        return new ResetPasswordResponseDto(result.UserId!.Value, result.UserStatus!);
    }

    private static BadRequestException InvalidOtp()
        => new("AUTH_OTP_INVALID", "Invalid verification code.");
}
