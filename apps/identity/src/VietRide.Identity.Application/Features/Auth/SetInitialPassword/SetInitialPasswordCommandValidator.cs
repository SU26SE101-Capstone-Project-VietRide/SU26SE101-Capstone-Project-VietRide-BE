using FluentValidation;

namespace VietRide.Identity.Application.Features.Auth.SetInitialPassword;

public sealed class SetInitialPasswordCommandValidator : AbstractValidator<SetInitialPasswordCommand>
{
    public SetInitialPasswordCommandValidator()
    {
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .Must(ContainLetter)
            .WithMessage("Password must contain at least one letter.")
            .Must(ContainDigit)
            .WithMessage("Password must contain at least one digit.");
    }

    private static bool ContainLetter(string? password) => !string.IsNullOrEmpty(password) && password.Any(char.IsLetter);

    private static bool ContainDigit(string? password) => !string.IsNullOrEmpty(password) && password.Any(char.IsDigit);
}
