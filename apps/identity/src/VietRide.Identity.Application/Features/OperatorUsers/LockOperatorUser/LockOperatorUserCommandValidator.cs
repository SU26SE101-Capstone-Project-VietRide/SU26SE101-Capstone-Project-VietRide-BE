using FluentValidation;

namespace VietRide.Identity.Application.Features.OperatorUsers.LockOperatorUser;

public sealed class LockOperatorUserCommandValidator : AbstractValidator<LockOperatorUserCommand>
{
    public LockOperatorUserCommandValidator()
    {
        RuleFor(command => command.CallerUserId).NotEmpty();
        RuleFor(command => command.CallerRole).NotEmpty();
        RuleFor(command => command.CallerOperatorId).NotNull().NotEqual(Guid.Empty);
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.IpAddress).MaximumLength(45);
        RuleFor(command => command.UserAgent).MaximumLength(500);
    }
}
