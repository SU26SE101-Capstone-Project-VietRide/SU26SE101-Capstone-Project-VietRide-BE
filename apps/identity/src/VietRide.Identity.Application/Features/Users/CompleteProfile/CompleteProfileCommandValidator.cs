using FluentValidation;

namespace VietRide.Identity.Application.Features.Users.CompleteProfile;

/// <summary>
/// Input-shape validation for <see cref="CompleteProfileCommand"/>.
/// Phone presence/format validation belongs in the handler so missing, empty,
/// whitespace, and bad-format values return the contract-specific
/// 400 AUTH_PHONE_INVALID_FORMAT.
/// </summary>
public sealed class CompleteProfileCommandValidator : AbstractValidator<CompleteProfileCommand>
{
    public CompleteProfileCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();
    }
}
