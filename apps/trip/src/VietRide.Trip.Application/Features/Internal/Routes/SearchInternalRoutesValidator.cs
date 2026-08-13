using FluentValidation;

namespace VietRide.Trip.Application.Features.Internal.Routes;

public sealed class SearchInternalRoutesValidator : AbstractValidator<SearchInternalRoutesQuery>
{
    public SearchInternalRoutesValidator()
    {
        RuleFor(query => query.OperatorId).NotEmpty();
        RuleFor(query => query.Search).NotEmpty().MaximumLength(255);
    }
}
