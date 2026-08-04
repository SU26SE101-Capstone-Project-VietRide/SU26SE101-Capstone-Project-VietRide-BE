using FluentValidation;

namespace VietRide.Trip.Application.Features.RouteChangeProposals;

public sealed class RejectRouteChangeProposalValidator : AbstractValidator<RejectRouteChangeProposalCommand>
{
    public RejectRouteChangeProposalValidator()
    {
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.ActorUserId).NotEmpty();
        RuleFor(x => x.ProposalId).NotEmpty();
        RuleFor(x => x.RejectionReason).MaximumLength(500);
    }
}
