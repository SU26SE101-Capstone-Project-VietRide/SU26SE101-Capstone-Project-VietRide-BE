using FluentValidation;

namespace VietRide.Identity.Application.Features.Admin.UnlockUser;

public sealed class UnlockUserCommandValidator : AbstractValidator<UnlockUserCommand>
{
    public UnlockUserCommandValidator()
    {
        RuleFor(command => command.CallerUserId).NotEmpty();
        RuleFor(command => command.CallerRole).NotEmpty();
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.IpAddress).MaximumLength(45);
        RuleFor(command => command.UserAgent).MaximumLength(500);
    }
}
