using FluentValidation;

namespace VietRide.Trip.Application.Features.RouteStopFareTemplates;

public sealed class ListRouteStopFareTemplatesValidator : AbstractValidator<ListRouteStopFareTemplatesQuery>
{
    public ListRouteStopFareTemplatesValidator()
    {
        RuleFor(query => query.OperatorId)
            .NotEmpty();
        RuleFor(query => query.RouteId)
            .NotEmpty();
        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(1)
            .When(query => query.Page.HasValue);
        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 100)
            .When(query => query.PageSize.HasValue);
    }
}
