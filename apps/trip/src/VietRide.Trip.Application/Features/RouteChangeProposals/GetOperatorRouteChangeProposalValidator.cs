using FluentValidation;

namespace VietRide.Trip.Application.Features.RouteChangeProposals;

public sealed class GetOperatorRouteChangeProposalValidator : AbstractValidator<GetOperatorRouteChangeProposalQuery>
{
    public GetOperatorRouteChangeProposalValidator()
    {
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.ProposalId).NotEmpty();
    }
}
