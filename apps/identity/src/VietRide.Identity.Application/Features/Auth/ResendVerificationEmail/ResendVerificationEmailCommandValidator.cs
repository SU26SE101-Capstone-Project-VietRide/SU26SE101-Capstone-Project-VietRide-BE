using FluentValidation;

namespace VietRide.Identity.Application.Features.Auth.ResendVerificationEmail;

public sealed class ResendVerificationEmailCommandValidator : AbstractValidator<ResendVerificationEmailCommand>
{
    public ResendVerificationEmailCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(320);

        RuleFor(x => x.Purpose)
            .NotEmpty();
    }
}
