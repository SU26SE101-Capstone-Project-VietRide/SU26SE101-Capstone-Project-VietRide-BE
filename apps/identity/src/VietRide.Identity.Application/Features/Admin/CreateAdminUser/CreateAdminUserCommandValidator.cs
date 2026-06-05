using FluentValidation;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Application.Features.Admin.CreateAdminUser;

/// <summary>Input-shape validation for <see cref="CreateAdminUserCommand"/>.</summary>
public sealed class CreateAdminUserCommandValidator : AbstractValidator<CreateAdminUserCommand>
{
    public CreateAdminUserCommandValidator()
    {
        RuleFor(x => x.CallerUserId)
            .NotEmpty();

        RuleFor(x => x.CallerRole)
            .NotEmpty();

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(320);

        RuleFor(x => x.DisplayName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.Role)
            .NotEmpty()
            .Must(role => string.Equals(role, UserRole.SYSTEM_ADMIN.ToString(), StringComparison.Ordinal))
            .WithMessage("Only SYSTEM_ADMIN can be created by this endpoint.");
    }
}
