using FluentValidation;

namespace VietRide.Identity.Application.Features.Admin.SuspendOperator;

public sealed class SuspendOperatorCommandValidator : AbstractValidator<SuspendOperatorCommand>
{
    public SuspendOperatorCommandValidator()
    {
        RuleFor(x => x.CallerRole).NotEmpty();
        RuleFor(x => x.CallerUserId).NotEmpty();
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}
