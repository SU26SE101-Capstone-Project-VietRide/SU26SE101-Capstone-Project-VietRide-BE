using FluentValidation;

namespace VietRide.Identity.Application.Features.Auth.ResetPassword;

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(320);

        RuleFor(x => x.Code)
            .NotEmpty()
            .Length(6)
            .Matches(@"^\d{6}$");

        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(128)
            .Must(ContainLetter)
            .WithMessage("Password must contain at least one letter.")
            .Must(ContainDigit)
            .WithMessage("Password must contain at least one digit.");
    }

    private static bool ContainLetter(string? password) => !string.IsNullOrEmpty(password) && password.Any(char.IsLetter);

    private static bool ContainDigit(string? password) => !string.IsNullOrEmpty(password) && password.Any(char.IsDigit);
}
