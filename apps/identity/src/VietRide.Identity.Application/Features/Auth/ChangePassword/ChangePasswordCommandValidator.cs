using FluentValidation;

namespace VietRide.Identity.Application.Features.Auth.ChangePassword;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();

        RuleFor(command => command.CurrentPassword)
            .NotEmpty()
            .MaximumLength(128);

        RuleFor(command => command.NewPassword)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(128)
            .Must(ContainLetter)
            .WithMessage("Password must contain at least one letter.")
            .Must(ContainDigit)
            .WithMessage("Password must contain at least one digit.");

        RuleFor(command => command.IpAddress).MaximumLength(45);
        RuleFor(command => command.UserAgent).MaximumLength(500);
    }

    private static bool ContainLetter(string? password)
        => !string.IsNullOrEmpty(password) && password.Any(char.IsLetter);

    private static bool ContainDigit(string? password)
        => !string.IsNullOrEmpty(password) && password.Any(char.IsDigit);
}
