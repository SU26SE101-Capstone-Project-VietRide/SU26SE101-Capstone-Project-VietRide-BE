using FluentValidation;

namespace VietRide.Identity.Application.Features.Internal.Operators.SearchOperatorCrew;

public sealed class SearchOperatorCrewQueryValidator : AbstractValidator<SearchOperatorCrewQuery>
{
    public SearchOperatorCrewQueryValidator()
    {
        RuleFor(query => query.OperatorId).NotEmpty();
        RuleFor(query => query.Search).NotEmpty().MaximumLength(255);
    }
}
