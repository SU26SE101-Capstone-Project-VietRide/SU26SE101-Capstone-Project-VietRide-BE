using FluentValidation;

namespace VietRide.Trip.Application.Features.Routes;

public sealed class ListRoutesValidator : AbstractValidator<ListRoutesQuery>
{
    public ListRoutesValidator()
    {
        RuleFor(query => query.OperatorId)
            .NotEmpty();
        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(1)
            .When(query => query.Page.HasValue);
        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 100)
            .When(query => query.PageSize.HasValue);
    }
}
