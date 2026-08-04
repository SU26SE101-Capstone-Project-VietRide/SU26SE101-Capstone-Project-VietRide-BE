using FluentValidation;

namespace VietRide.Trip.Application.Features.RouteChangeProposals;

public sealed class ApproveRouteChangeProposalValidator : AbstractValidator<ApproveRouteChangeProposalCommand>
{
    public ApproveRouteChangeProposalValidator()
    {
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.ActorUserId).NotEmpty();
        RuleFor(x => x.ProposalId).NotEmpty();
    }
}
