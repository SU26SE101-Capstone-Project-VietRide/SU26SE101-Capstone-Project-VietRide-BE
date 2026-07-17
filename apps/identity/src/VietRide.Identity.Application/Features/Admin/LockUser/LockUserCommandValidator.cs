using FluentValidation;

namespace VietRide.Identity.Application.Features.Admin.LockUser;

public sealed class LockUserCommandValidator : AbstractValidator<LockUserCommand>
{
    public LockUserCommandValidator()
    {
        RuleFor(command => command.CallerUserId).NotEmpty();
        RuleFor(command => command.CallerRole).NotEmpty();
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.IpAddress).MaximumLength(45);
        RuleFor(command => command.UserAgent).MaximumLength(500);
    }
}
