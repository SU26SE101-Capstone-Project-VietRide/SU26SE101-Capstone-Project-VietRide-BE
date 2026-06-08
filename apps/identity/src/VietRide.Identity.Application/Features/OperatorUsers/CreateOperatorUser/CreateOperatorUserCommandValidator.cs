using FluentValidation;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Application.Features.OperatorUsers.CreateOperatorUser;

public sealed class CreateOperatorUserCommandValidator : AbstractValidator<CreateOperatorUserCommand>
{
    private static readonly string[] AllowedRoles =
    [
        UserRole.DRIVER.ToString(),
        UserRole.ASSISTANT.ToString(),
        UserRole.OPERATOR_STAFF.ToString(),
    ];

    public CreateOperatorUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.Phone).NotEmpty().Matches("^\\+84[0-9]{9,10}$");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Role).NotEmpty().Must(role => AllowedRoles.Contains(role));
    }
}
