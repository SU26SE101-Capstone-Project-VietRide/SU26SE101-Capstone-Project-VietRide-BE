using FluentValidation;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed class DecideParcelClaimAppealCommandValidator
    : AbstractValidator<DecideParcelClaimAppealCommand>
{
    public DecideParcelClaimAppealCommandValidator()
    {
        RuleFor(x => x.AppealId).NotEmpty();
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.DecidedByUserId).NotEmpty();
        RuleFor(x => x.Decision).Must(decision => decision is "UPHOLD" or "APPROVE_ADJUSTMENT");
        RuleFor(x => x.RevisedProvenDirectLossVnd).GreaterThanOrEqualTo(0).When(x => x.RevisedProvenDirectLossVnd.HasValue);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
    }
}
