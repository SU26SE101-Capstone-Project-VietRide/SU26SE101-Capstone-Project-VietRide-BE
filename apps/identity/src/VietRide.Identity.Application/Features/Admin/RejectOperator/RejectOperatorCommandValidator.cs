using FluentValidation;

namespace VietRide.Identity.Application.Features.Admin.RejectOperator;

public sealed class RejectOperatorCommandValidator : AbstractValidator<RejectOperatorCommand>
{
    public RejectOperatorCommandValidator()
    {
        RuleFor(x => x.CallerRole).NotEmpty();
        RuleFor(x => x.CallerUserId).NotEmpty();
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}
