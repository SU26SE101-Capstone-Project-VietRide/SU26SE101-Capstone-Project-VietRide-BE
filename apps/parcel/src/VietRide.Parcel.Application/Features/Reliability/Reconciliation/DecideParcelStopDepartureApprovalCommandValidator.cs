using FluentValidation;

namespace VietRide.Parcel.Application.Features.Reliability.Reconciliation;

public sealed class DecideParcelStopDepartureApprovalCommandValidator
    : AbstractValidator<DecideParcelStopDepartureApprovalCommand>
{
    public DecideParcelStopDepartureApprovalCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.ReviewerUserId).NotEmpty();
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.ReviewerRole)
            .Must(role => role is "DRIVER" or "OPERATOR_STAFF" or "OPERATOR_ADMIN");
        RuleFor(x => x.Decision)
            .Must(decision => decision is "APPROVE" or "REJECT");
    }
}
