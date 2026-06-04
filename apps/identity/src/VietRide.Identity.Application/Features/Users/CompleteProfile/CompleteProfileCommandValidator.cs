using FluentValidation;

namespace VietRide.Identity.Application.Features.Users.CompleteProfile;

/// <summary>
/// Input-shape validation for <see cref="CompleteProfileCommand"/>.
/// Phone format validation belongs in the handler so invalid format returns
/// the contract-specific 400 AUTH_PHONE_INVALID_FORMAT.
/// </summary>
public sealed class CompleteProfileCommandValidator : AbstractValidator<CompleteProfileCommand>
{
    public CompleteProfileCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.Phone)
            .NotEmpty();
    }
}
