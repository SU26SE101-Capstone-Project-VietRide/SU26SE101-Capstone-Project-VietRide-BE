using FluentValidation;

namespace VietRide.Trip.Application.Features.RouteChangeProposals;

public sealed class ListAssignedTripAlternativeRoutesValidator : AbstractValidator<ListAssignedTripAlternativeRoutesQuery>
{
    public ListAssignedTripAlternativeRoutesValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Page).InclusiveBetween(1, 100).When(x => x.Page.HasValue);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).When(x => x.PageSize.HasValue);
    }
}
