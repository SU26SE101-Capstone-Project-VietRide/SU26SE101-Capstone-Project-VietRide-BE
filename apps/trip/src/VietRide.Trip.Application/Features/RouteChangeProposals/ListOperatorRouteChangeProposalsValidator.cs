using FluentValidation;

namespace VietRide.Trip.Application.Features.RouteChangeProposals;

public sealed class ListOperatorRouteChangeProposalsValidator : AbstractValidator<ListOperatorRouteChangeProposalsQuery>
{
    public ListOperatorRouteChangeProposalsValidator()
    {
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.Page).InclusiveBetween(1, 100).When(x => x.Page.HasValue);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).When(x => x.PageSize.HasValue);
    }
}
