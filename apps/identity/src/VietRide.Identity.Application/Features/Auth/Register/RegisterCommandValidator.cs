using FluentValidation;

namespace VietRide.Identity.Application.Features.Auth.Register;

/// <summary>
/// Input-shape validation for <see cref="RegisterCommand"/>.
/// Phone is required here; semantic phone-format validation belongs in the handler
/// so the API can return the contract-specific 400 AUTH_PHONE_INVALID_FORMAT.
/// </summary>
public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(320);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(128);

        RuleFor(x => x.DisplayName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.Phone)
            .NotEmpty();
    }
}
