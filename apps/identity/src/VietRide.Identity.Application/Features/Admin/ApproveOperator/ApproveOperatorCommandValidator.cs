using FluentValidation;

namespace VietRide.Identity.Application.Features.Admin.ApproveOperator;

public sealed class ApproveOperatorCommandValidator : AbstractValidator<ApproveOperatorCommand>
{
    public ApproveOperatorCommandValidator()
    {
        RuleFor(x => x.CallerRole).NotEmpty();
        RuleFor(x => x.CallerUserId).NotEmpty();
        RuleFor(x => x.OperatorId).NotEmpty();
    }
}
