using FluentValidation;

namespace VietRide.Identity.Application.Features.Auth.Register;

/// <summary>
/// Input-shape validation for <see cref="RegisterCommand"/>.
/// Phone: local (0xxxxxxxxx / 0xxxxxxxxxx) OR E.164 (+84xxxxxxxxx / +84xxxxxxxxxx).
/// Other formats → FluentValidation error → 422 VALIDATION_ERROR.
/// Note: AUTH_PHONE_INVALID_FORMAT (400) is a semantic duplicate that the contract
/// references; FluentValidation 422 covers it at the input-validation boundary.
/// </summary>
public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    // Matches local VN: 0 followed by 9 or 10 digits.
    private const string LocalVnPattern = @"^0[0-9]{9,10}$";

    // Matches E.164 VN: +84 followed by 9 or 10 digits.
    private const string E164VnPattern = @"^\+84[0-9]{9,10}$";

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
            .NotEmpty()
            .Matches($"({LocalVnPattern})|({E164VnPattern})")
            .WithErrorCode("AUTH_PHONE_INVALID_FORMAT")
            .WithMessage("Phone must be a valid Vietnamese number (0xxxxxxxxx or +84xxxxxxxxx).");
    }
}
