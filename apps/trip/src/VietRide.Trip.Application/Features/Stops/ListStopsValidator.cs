using FluentValidation;

namespace VietRide.Trip.Application.Features.Stops;

public sealed class ListStopsValidator : AbstractValidator<ListStopsQuery>
{
    public ListStopsValidator()
    {
        RuleFor(query => query.OperatorId)
            .NotEmpty();
        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(1)
            .When(query => query.Page.HasValue);
        RuleFor(query => query.PageSize)
            .GreaterThanOrEqualTo(1)
            .When(query => query.PageSize.HasValue);
        RuleFor(query => query.Search)
            .MaximumLength(255)
            .When(query => !string.IsNullOrWhiteSpace(query.Search));
    }
}
